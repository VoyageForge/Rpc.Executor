using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace VoyageForge.Rpc.CodeGen
{
    /// <summary>为 Session 生成远端 API 代理，为本地 Controller 生成请求分发代码。</summary>
    [Generator]
    public sealed class ControllerRpcGenerator : ISourceGenerator
    {
        private const string ContractAttr = "VoyageForge.Rpc.Transport.Session.RpcContractAttribute";
        private const string SessionAttr = "VoyageForge.Rpc.Transport.Session.RpcSessionAttribute";
        private const string ApiAttr = "VoyageForge.Rpc.Transport.Session.RpcApiAttribute";
        private const string RpcMethodAttr = "VoyageForge.Rpc.Executor.RpcMethodAttribute";
        private const string RpcControllerAttr = "VoyageForge.Rpc.Executor.RpcControllerAttribute";
        private const string ControllerBase = "VoyageForge.Rpc.Executor.RpcControllerBase";
        private const string CancellationTokenName = "System.Threading.CancellationToken";
        private static readonly SymbolDisplayFormat Fqn = SymbolDisplayFormat.FullyQualifiedFormat;
        private static readonly SymbolDisplayFormat ProtocolFqn = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions & ~SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
        private static readonly DiagnosticDescriptor Invalid = new DiagnosticDescriptor("RPC100", "RPC 声明无效", "RPC 声明无效：{0}", "VoyageForge.RPC", DiagnosticSeverity.Error, true);
        private static readonly DiagnosticDescriptor Unsupported = new DiagnosticDescriptor("RPC101", "RPC 方法不受支持", "RPC 方法“{0}”不受支持：{1}", "VoyageForge.RPC", DiagnosticSeverity.Error, true);
        private static readonly DiagnosticDescriptor Collision = new DiagnosticDescriptor("RPC102", "MethodId 冲突", "RPC MethodId 冲突：{0}", "VoyageForge.RPC", DiagnosticSeverity.Error, true);

        public void Initialize(GeneratorInitializationContext context) => context.RegisterForSyntaxNotifications(() => new Receiver());

        public void Execute(GeneratorExecutionContext context)
        {
            Receiver receiver = context.SyntaxReceiver as Receiver;
            if (receiver == null) return;
            Dictionary<string, INamedTypeSymbol> types = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            foreach (TypeDeclarationSyntax syntax in receiver.Types)
            {
                INamedTypeSymbol symbol = context.Compilation.GetSemanticModel(syntax.SyntaxTree).GetDeclaredSymbol(syntax) as INamedTypeSymbol;
                if (symbol != null) types[symbol.ToDisplayString(Fqn)] = symbol;
            }

            List<ControllerModel> controllers = new List<ControllerModel>();
            foreach (INamedTypeSymbol type in types.Values)
            {
                AttributeData attr = Attribute(type, RpcControllerAttr);
                if (attr == null) continue;
                ControllerModel model = BuildController(context, type, attr);
                if (model != null) controllers.Add(model);
            }

            CSharpParseOptions dtoParseOptions = context.ParseOptions as CSharpParseOptions
                                                 ?? new CSharpParseOptions(LanguageVersion.CSharp9);
            RpcDtoMemoryPackEmitter dtoEmitter = new RpcDtoMemoryPackEmitter(
                context.Compilation, dtoParseOptions, context.CancellationToken);
            HashSet<string> generatedDtos = new HashSet<string>(StringComparer.Ordinal);

            // 每个 Session 自己声明 API 集合；代理属性和 Create 工厂由本生成器生成。
            foreach (INamedTypeSymbol type in types.Values)
            {
                if (Attribute(type, SessionAttr) != null)
                    EmitSession(context, type, dtoEmitter, generatedDtos);
            }

            if (controllers.Count > 0 && !HasCollision(context, controllers) &&
                PrepareControllerDtos(context, controllers, dtoEmitter, generatedDtos))
            {
                foreach (ControllerModel controller in controllers) EmitDispatcher(context, controller);
                EmitCatalog(context, controllers);
            }
        }

        private sealed class Receiver : ISyntaxReceiver
        {
            public readonly List<TypeDeclarationSyntax> Types = new List<TypeDeclarationSyntax>();
            public void OnVisitSyntaxNode(SyntaxNode node) { if (node is TypeDeclarationSyntax type) Types.Add(type); }
        }
        private sealed class ControllerModel
        {
            public INamedTypeSymbol Type;
            public string Name;
            public readonly List<MethodModel> Methods = new List<MethodModel>();
        }
        private sealed class MethodModel
        {
            public IMethodSymbol Implementation;
            public string Name, Signature, DispatchName;
            public ulong Id;
            public List<IParameterSymbol> Args;
            public IParameterSymbol Cancellation;
            public ITypeSymbol Result;
            public bool HasResult;
            /// <summary>请求 DTO 的完整类型名；0 参数方法为 null（走空 Payload）。</summary>
            public string DtoFqn;
        }

        private static ControllerModel BuildController(GeneratorExecutionContext context, INamedTypeSymbol type, AttributeData attribute)
        {
            if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsGenericType || type.ContainingType != null || !IsPartial(type) || !Derives(type, ControllerBase))
            { Report(context, Invalid, type, type.Name + " 必须是非抽象、非泛型、partial class 且继承 RpcControllerBase。"); return null; }

            string controllerName = attribute.ConstructorArguments.Length > 0
                ? attribute.ConstructorArguments[0].Value as string : null;
            if (string.IsNullOrWhiteSpace(controllerName)) controllerName = type.Name.EndsWith("Controller", StringComparison.Ordinal)
                ? type.Name.Substring(0, type.Name.Length - "Controller".Length) : type.Name;

            ControllerModel model = new ControllerModel { Type = type, Name = controllerName };
            foreach (IMethodSymbol method in type.GetMembers().OfType<IMethodSymbol>())
            {
                AttributeData rpcMethod = Attribute(method, RpcMethodAttr);
                if (rpcMethod == null) continue;
                if (method.DeclaredAccessibility != Accessibility.Public)
                { Report(context, Invalid, method, "[RpcMethod] 方法必须是 public 实例方法。"); return null; }
                List<IParameterSymbol> args; IParameterSymbol cancellation; ITypeSymbol result; bool hasResult; string error;
                if (!ValidateMethod(method, out args, out cancellation, out result, out hasResult, out error))
                { Report(context, Unsupported, method, method.Name, error); return null; }
                string methodName = rpcMethod.ConstructorArguments.Length > 0
                    ? rpcMethod.ConstructorArguments[0].Value as string : null;
                if (string.IsNullOrWhiteSpace(methodName)) methodName = method.Name;
                ulong id = Hash(CanonicalKey(model.Name, methodName, args));
                model.Methods.Add(new MethodModel
                {
                    Implementation = method, Name = methodName,
                    Args = args, Cancellation = cancellation, Result = result, HasResult = hasResult,
                    Id = id, Signature = Signature(args), DispatchName = "__RpcDispatch_" + id.ToString("X16")
                });
            }
            if (model.Methods.Count == 0)
            { Report(context, Invalid, type, type.Name + " 没有声明任何 [RpcMethod] 方法。"); return null; }
            return model;
        }

        private static void EmitSession(GeneratorExecutionContext context, INamedTypeSymbol session,
            RpcDtoMemoryPackEmitter dtoEmitter, HashSet<string> generatedDtos)
        {
            if (session.TypeKind != TypeKind.Class || session.IsAbstract || session.IsGenericType || session.ContainingType != null ||
                !IsPartial(session) || session.BaseType == null || session.BaseType.ToDisplayString() != "VoyageForge.Rpc.Transport.Session.Session")
            { Report(context, Invalid, session, session.Name + " 必须是非抽象、非泛型 partial class 且直接继承 Session。"); return; }

            List<INamedTypeSymbol> contracts = session.GetAttributes()
                .Where(a => a.AttributeClass != null && a.AttributeClass.ToDisplayString() == ApiAttr)
                .Select(a => a.ConstructorArguments.Length > 0 ? a.ConstructorArguments[0].Value as INamedTypeSymbol : null)
                .Where(t => t != null).ToList();
            if (session.InstanceConstructors.Any(c => !c.IsImplicitlyDeclared))
            { Report(context, Invalid, session, session.Name + " 不允许自行声明构造函数；实例必须通过生成的 Create() 创建。"); return; }
            if (session.GetMembers("Create").Length > 0)
            { Report(context, Invalid, session, session.Name + " 不允许自行声明 Create 成员；该工厂由生成器提供。"); return; }

            HashSet<string> generatedMemberNames = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> proxyTypeNames = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<ulong, string> methodIds = new Dictionary<ulong, string>();
            foreach (INamedTypeSymbol contract in contracts)
            {
                if (contract.TypeKind != TypeKind.Interface || contract.IsGenericType || contract.ContainingType != null ||
                    Attribute(contract, ContractAttr) == null || contract.DeclaredAccessibility != Accessibility.Public)
                { Report(context, Invalid, session, "RpcApi 只能引用顶层、非泛型、public 且带 [RpcContract] 的接口。"); return; }
                if (contract.AllInterfaces.Concat(new[] { contract }).SelectMany(i => i.GetMembers()).Any(m => m is IPropertySymbol || m is IEventSymbol))
                { Report(context, Invalid, contract, contract.Name + " RPC 契约只允许声明方法，不支持属性和事件。"); return; }
                string propertyName = ControllerPropertyName(contract);
                string fieldName = "_" + char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
                string proxyName = "__RpcProxy_" + SafeName(contract.ToDisplayString(Fqn));
                if (!SyntaxFacts.IsValidIdentifier(propertyName) || !generatedMemberNames.Add(propertyName) ||
                    !generatedMemberNames.Add(fieldName) || HasMemberInHierarchy(session, propertyName) ||
                    HasMemberInHierarchy(session, fieldName) || !proxyTypeNames.Add(proxyName) || session.GetTypeMembers(proxyName).Length > 0)
                { Report(context, Invalid, session, "Session API 属性、字段或代理类型名称无效、重复或与用户成员冲突：" + propertyName); return; }
                foreach (IMethodSymbol method in Methods(contract))
                {
                    List<IParameterSymbol> args; IParameterSymbol cancellation; ITypeSymbol result; bool hasResult; string error;
                    if (!ValidateMethod(method, out args, out cancellation, out result, out hasResult, out error))
                    { Report(context, Unsupported, method, method.Name, error); return; }
                    ulong id = Hash(CanonicalKey(ControllerName(contract), method.Name, args));
                    string key = ControllerName(contract) + "." + method.Name + Signature(args);
                    if (methodIds.TryGetValue(id, out string previous))
                    { Report(context, Collision, session, previous + " 与 " + key + "（Session API 重复）"); return; }
                    methodIds.Add(id, key);
                }
            }

            // 先生成并验证当前 Session 的全部请求 DTO。任一失败就不产出半成品代理。
            foreach (INamedTypeSymbol contract in contracts)
            foreach (IMethodSymbol method in Methods(contract))
            {
                List<IParameterSymbol> args; IParameterSymbol cancellation; ITypeSymbol result; bool hasResult; string error;
                if (!ValidateMethod(method, out args, out cancellation, out result, out hasResult, out error)) return;
                string dto = RpcDtoFactory.EnsureDto(context, dtoEmitter, ControllerName(contract), method.Name, args, generatedDtos);
                if (args.Count > 0 && dto == null) return;
            }

            StringBuilder s = new StringBuilder(); BeginNamespace(s, session.ContainingNamespace);
            s.Append("partial class ").Append(Id(session.Name)).AppendLine("\n{");
            List<string> proxyNames = new List<string>();
            for (int i = 0; i < contracts.Count; i++)
            {
                INamedTypeSymbol contract = contracts[i];
                string propertyName = ControllerPropertyName(contract);
                string fieldName = "_" + char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
                string proxyName = "__RpcProxy_" + SafeName(contract.ToDisplayString(Fqn));
                proxyNames.Add(proxyName);
                s.Append("    private readonly ").Append(TypeName(contract)).Append(' ').Append(Id(fieldName)).AppendLine(";");
                s.Append("    public ").Append(TypeName(contract)).Append(' ').Append(Id(propertyName)).Append(" => ").Append(Id(fieldName)).AppendLine(";");
            }
            s.Append("    private ").Append(Id(session.Name)).AppendLine("(global::VoyageForge.Rpc.Transport.Session.RpcControllerRegistry registry, int maxFrameBytes)");
            s.AppendLine("        : base(registry, maxFrameBytes)");
            s.AppendLine("    {");
            for (int i = 0; i < contracts.Count; i++)
            {
                string propertyName = ControllerPropertyName(contracts[i]);
                string fieldName = "_" + char.ToLowerInvariant(propertyName[0]) + propertyName.Substring(1);
                s.Append("        this.").Append(Id(fieldName)).Append(" = new ").Append(proxyNames[i]).AppendLine("(this);");
            }
            s.AppendLine("    }");
            s.Append("    public static ").Append(Id(session.Name)).Append(" Create(global::System.Net.Sockets.TcpClient client = null, global::VoyageForge.Rpc.Transport.Session.RpcControllerRegistry registry = null, int maxFrameBytes = global::VoyageForge.Rpc.Transport.Session.Session.DefaultMaxFrameBytes)").AppendLine("\n    {");
            s.Append("        var session = new ").Append(Id(session.Name)).AppendLine("(registry, maxFrameBytes);");
            s.AppendLine("        if (client != null) session.Attach(client);");
            s.AppendLine("        return session;");
            s.AppendLine("    }");
            for (int i = 0; i < contracts.Count; i++) EmitProxy(s, contracts[i], proxyNames[i], context, dtoEmitter, generatedDtos);
            s.AppendLine("}"); EndNamespace(s, session.ContainingNamespace);
            context.AddSource(SafeName(session.ToDisplayString(Fqn)) + ".RpcSession.g.cs", SourceText.From(s.ToString(), Encoding.UTF8));
        }

        private static bool ValidateMethod(IMethodSymbol method, out List<IParameterSymbol> args,
            out IParameterSymbol cancellation, out ITypeSymbol result, out bool hasResult, out string error)
        {
            args = new List<IParameterSymbol>(); cancellation = null; result = null; hasResult = false; error = null;
            if (method.MethodKind != MethodKind.Ordinary || method.IsStatic || method.IsGenericMethod)
            { error = "只支持非泛型实例方法。"; return false; }
            foreach (IParameterSymbol parameter in method.Parameters)
            {
                if (parameter.RefKind != RefKind.None) { error = "不支持 ref/out/in 参数。"; return false; }
                if (parameter.Type.TypeKind == TypeKind.Pointer || parameter.Type.TypeKind == TypeKind.FunctionPointer || parameter.Type.IsRefLikeType)
                { error = "不支持指针、函数指针或 ref struct 参数。"; return false; }
                if (parameter.Type.ToDisplayString() == CancellationTokenName)
                {
                    if (cancellation != null || !SymbolEqualityComparer.Default.Equals(parameter, method.Parameters.Last()))
                    { error = "CancellationToken 最多一个，且必须是最后一个参数。"; return false; }
                    cancellation = parameter;
                }
                else args.Add(parameter);
            }
            if (args.Count > 7) { error = "最多支持 7 个业务参数。"; return false; }
            INamedTypeSymbol task = method.ReturnType as INamedTypeSymbol;
            if (task == null || task.ContainingNamespace.ToDisplayString() != "System.Threading.Tasks" || task.Name != "Task")
            { error = "只支持 Task 或 Task<T> 返回类型。"; return false; }
            hasResult = task.Arity == 1;
            if (task.Arity > 1) { error = "不支持该 Task 返回类型。"; return false; }
            if (hasResult)
            {
                result = task.TypeArguments[0];
                if (result.TypeKind == TypeKind.Pointer || result.TypeKind == TypeKind.FunctionPointer || result.IsRefLikeType)
                { error = "不支持指针、函数指针或 ref struct 返回类型。"; return false; }
            }
            return true;
        }

        private static void EmitProxy(StringBuilder s, INamedTypeSymbol contract, string proxy,
            GeneratorExecutionContext context, RpcDtoMemoryPackEmitter dtoEmitter, HashSet<string> generatedDtos)
        {
            s.Append("    private sealed class ").Append(proxy).Append(" : ").Append(TypeName(contract)).AppendLine("\n    {");
            s.AppendLine("        private readonly global::VoyageForge.Rpc.Transport.Session.Session _session;");
            s.Append("        public ").Append(proxy).AppendLine("(global::VoyageForge.Rpc.Transport.Session.Session session) { _session = session; }");
            foreach (IMethodSymbol method in Methods(contract))
            {
                List<IParameterSymbol> args; IParameterSymbol cancellation; ITypeSymbol result; bool hasResult; string error;
                if (!ValidateMethod(method, out args, out cancellation, out result, out hasResult, out error)) continue;
                ulong id = Hash(CanonicalKey(ControllerName(contract), method.Name, args));

                // DTO 是代理实现细节；两端按相同协议签名生成相同的内部类型名和字段布局。
                string dtoFqn = RpcDtoFactory.EnsureDto(context, dtoEmitter, ControllerName(contract), method.Name, args, generatedDtos);
                if (args.Count > 0 && dtoFqn == null) continue; // DTO 生成失败，诊断已报，跳过该方法

                s.Append("        public ").Append(TypeName(method.ReturnType)).Append(' ').Append(Id(method.Name)).Append('(')
                    .Append(Parameters(method.Parameters)).AppendLine(")\n        {");
                s.Append("            byte[] __payload = ").Append(SerializeArgs(dtoFqn, args)).AppendLine(";");
                string ct = cancellation == null ? "default(global::System.Threading.CancellationToken)" : Id(cancellation.Name);
                if (hasResult) s.Append("            return _session.InvokeAsync<").Append(TypeName(result)).Append(">(0x").Append(id.ToString("X16")).Append("UL, __payload, ").Append(ct).AppendLine(");");
                else s.Append("            return _session.InvokeAsync(0x").Append(id.ToString("X16")).Append("UL, __payload, ").Append(ct).AppendLine(");");
                s.AppendLine("        }");
            }
            s.AppendLine("    }");
        }

        private static bool PrepareControllerDtos(GeneratorExecutionContext context, List<ControllerModel> controllers,
            RpcDtoMemoryPackEmitter dtoEmitter, HashSet<string> generatedDtos)
        {
            // 必须在输出任意 Dispatcher / Catalog 之前完成全部 DTO 预检，避免 Catalog 引用缺失的分发方法。
            foreach (ControllerModel controller in controllers)
            foreach (MethodModel method in controller.Methods)
            {
                method.DtoFqn = RpcDtoFactory.EnsureDto(context, dtoEmitter, controller.Name, method.Name, method.Args, generatedDtos);
                if (method.Args.Count > 0 && method.DtoFqn == null) return false;
            }
            return true;
        }

        private static void EmitDispatcher(GeneratorExecutionContext context, ControllerModel controller)
        {
            StringBuilder s = new StringBuilder(); BeginNamespace(s, controller.Type.ContainingNamespace);
            s.Append("partial class ").Append(Id(controller.Type.Name)).AppendLine("\n{");
            foreach (MethodModel method in controller.Methods)
            {
                s.Append("    internal async global::System.Threading.Tasks.Task<byte[]> ").Append(method.DispatchName)
                    .AppendLine("(byte[] payload, global::System.Threading.CancellationToken ct)\n    {");
                List<string> args = new List<string>();
                if (method.Args.Count > 0)
                {
                    // 反序列化整个请求 DTO，再按属性名取回各参数。
                    s.Append("        var __req = global::MemoryPack.MemoryPackSerializer.Deserialize<")
                        .Append(method.DtoFqn).AppendLine(">(payload);");
                    for (int i = 0; i < method.Args.Count; i++)
                    {
                        args.Add("__req.Arg" + i);
                    }
                }
                if (method.Cancellation != null) args.Add("ct");
                string call = "this." + Id(method.Implementation.Name) + "(" + string.Join(", ", args) + ")";
                if (method.HasResult)
                {
                    s.Append("        ").Append(TypeName(method.Result)).Append(" __result = await ").Append(call).AppendLine(".ConfigureAwait(false);");
                    s.AppendLine("        return global::MemoryPack.MemoryPackSerializer.Serialize(__result);");
                }
                else { s.Append("        await ").Append(call).AppendLine(".ConfigureAwait(false);"); s.AppendLine("        return global::System.Array.Empty<byte>();"); }
                s.AppendLine("    }");
            }
            s.AppendLine("}"); EndNamespace(s, controller.Type.ContainingNamespace);
            context.AddSource(SafeName(controller.Type.ToDisplayString(Fqn)) + ".RpcDispatcher.g.cs", SourceText.From(s.ToString(), Encoding.UTF8));
        }

        private static void EmitCatalog(GeneratorExecutionContext context, List<ControllerModel> controllers)
        {
            StringBuilder s = new StringBuilder();
            string catalogNamespace = "VoyageForge.Rpc.Generated." + SafeName(context.Compilation.AssemblyName ?? "Assembly");
            s.Append("namespace ").Append(catalogNamespace).AppendLine("\n{");
            s.AppendLine("    public static class GeneratedRpcControllerCatalog\n    {");
            s.AppendLine("        public static void Register(global::VoyageForge.Rpc.Transport.Session.RpcControllerRegistry registry, global::System.Func<global::System.Type, global::VoyageForge.Rpc.Executor.RpcControllerBase> controllerFactory)\n        {");
            s.AppendLine("            if (registry == null) throw new global::System.ArgumentNullException(nameof(registry));");
            s.AppendLine("            if (controllerFactory == null) throw new global::System.ArgumentNullException(nameof(controllerFactory));");
            foreach (ControllerModel controller in controllers)
            foreach (MethodModel method in controller.Methods)
            {
                s.AppendLine("            registry.Register(new global::VoyageForge.Rpc.Transport.Session.RpcMethodDescriptor");
                s.AppendLine("            {");
                s.Append("                MethodId = 0x").Append(method.Id.ToString("X16")).AppendLine("UL,");
                s.Append("                ControllerName = ").Append(Lit(controller.Name)).AppendLine(",");
                s.Append("                MethodName = ").Append(Lit(method.Name)).AppendLine(",");
                s.Append("                Signature = ").Append(Lit(method.Signature)).AppendLine(",");
                s.Append("                Handler = (session, payload, ct) => { var instance = controllerFactory(typeof(").Append(TypeName(controller.Type)).AppendLine("));");
                s.Append("                    if (!(instance is ").Append(TypeName(controller.Type)).Append(" controller)) throw new global::System.InvalidOperationException(\"Controller factory 返回了错误类型：").Append(TypeName(controller.Type)).AppendLine("\");");
                s.AppendLine("                    controller.SetSession(session);");
                s.Append("                    return controller.").Append(method.DispatchName).AppendLine("(payload, ct); }");
                s.AppendLine("            });");
            }
            s.AppendLine("        }");
            s.AppendLine("        public static global::VoyageForge.Rpc.Transport.Session.RpcControllerRegistry CreateRegistry(global::System.Func<global::System.Type, global::VoyageForge.Rpc.Executor.RpcControllerBase> controllerFactory)");
            s.AppendLine("        {");
            s.AppendLine("            var registry = new global::VoyageForge.Rpc.Transport.Session.RpcControllerRegistry();");
            s.AppendLine("            Register(registry, controllerFactory);");
            s.AppendLine("            return registry;");
            s.AppendLine("        }");
            s.AppendLine("    }\n}");
            context.AddSource("GeneratedRpcControllerCatalog.g.cs", SourceText.From(s.ToString(), Encoding.UTF8));
        }

        private static bool HasCollision(GeneratorExecutionContext context, List<ControllerModel> controllers)
        {
            Dictionary<ulong, string> ids = new Dictionary<ulong, string>(); bool collision = false;
            foreach (ControllerModel c in controllers) foreach (MethodModel m in c.Methods)
            {
                string key = c.Name + "." + m.Name + m.Signature;
                if (ids.TryGetValue(m.Id, out string old))
                { Report(context, Collision, c.Type, old + " 与 " + key + "（包括重复注册）"); collision = true; }
                else ids[m.Id] = key;
            }
            return collision;
        }

        private static IEnumerable<IMethodSymbol> Methods(INamedTypeSymbol contract)
        {
            List<INamedTypeSymbol> all = contract.AllInterfaces.ToList(); all.Add(contract);
            return all.SelectMany(i => i.GetMembers().OfType<IMethodSymbol>())
                .Where(m => m.MethodKind == MethodKind.Ordinary && m.DeclaredAccessibility == Accessibility.Public)
                .GroupBy(MethodIdentity, StringComparer.Ordinal).Select(g => g.First())
                .OrderBy(m => m.Name, StringComparer.Ordinal).ThenBy(MethodIdentity, StringComparer.Ordinal);
        }
        /// <summary>
        /// 生成调用端的 payload 表达式：把方法参数打包成一个请求 DTO 再序列化。
        /// </summary>
        /// <remarks>
        /// <para>DTO 使用 Arg0、Arg1 等位置字段；参数名不会进入协议，因此双端允许使用不同的局部参数名。</para>
        /// <para>0 参数保持空 payload —— 没有请求体，不生成 DTO。</para>
        /// </remarks>
        private static string SerializeArgs(string dtoFqn, List<IParameterSymbol> args)
        {
            if (args.Count == 0) return "global::System.Array.Empty<byte>()";
            string assignments = string.Join(", ", args.Select((a, index) => "Arg" + index + " = " + Id(a.Name)));
            return "global::MemoryPack.MemoryPackSerializer.Serialize(new " + dtoFqn + " { " + assignments + " })";
        }
        private static string MethodIdentity(IMethodSymbol method) => method.Name + "(" +
            string.Join(",", method.Parameters.Select(p => p.Type.ToDisplayString(ProtocolFqn))) + ")->" + method.ReturnType.ToDisplayString(ProtocolFqn);
        private static string Signature(List<IParameterSymbol> args) => "(" + string.Join(", ", args.Select(a => a.Type.ToDisplayString(ProtocolFqn))) + ")";
        private static string CanonicalKey(string name, string method, List<IParameterSymbol> args) =>
            "v2\u001F" + name + "\u001F" + method + "\u001F" + string.Join("\u001E", args.Select(a => a.Type.ToDisplayString(ProtocolFqn)));
        private static ulong Hash(string value)
        {
            const ulong offset = 14695981039346656037UL, prime = 1099511628211UL;
            ulong hash = offset; byte[] bytes = Encoding.UTF8.GetBytes(value);
            foreach (byte b in bytes) { hash ^= b; hash = unchecked(hash * prime); } return hash;
        }
        private static string ControllerPropertyName(INamedTypeSymbol contract)
        {
            return contract.Name.StartsWith("I", StringComparison.Ordinal) && contract.Name.Length > 1
                ? contract.Name.Substring(1) : contract.Name;
        }
        private static string ControllerName(INamedTypeSymbol contract)
        {
            AttributeData a = Attribute(contract, ContractAttr);
            string name = a != null && a.ConstructorArguments.Length > 0 ? a.ConstructorArguments[0].Value as string : null;
            if (!string.IsNullOrWhiteSpace(name)) return name;

            string fallback = contract.Name.StartsWith("I", StringComparison.Ordinal) && contract.Name.Length > 1
                ? contract.Name.Substring(1) : contract.Name;
            if (fallback.EndsWith("Api", StringComparison.Ordinal) && fallback.Length > 3)
                fallback = fallback.Substring(0, fallback.Length - 3);
            return fallback;
        }
        private static AttributeData Attribute(ISymbol symbol, string fullName) => symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass != null && a.AttributeClass.ToDisplayString() == fullName);
        private static bool Derives(INamedTypeSymbol type, string fullName)
        { for (INamedTypeSymbol t = type; t != null; t = t.BaseType) if (t.ToDisplayString() == fullName) return true; return false; }
        private static bool HasMemberInHierarchy(INamedTypeSymbol type, string name)
        { for (INamedTypeSymbol t = type; t != null; t = t.BaseType) if (t.GetMembers(name).Length > 0) return true; return false; }
        private static bool IsPartial(INamedTypeSymbol type) => type.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<TypeDeclarationSyntax>().Any(s => s.Modifiers.Any(SyntaxKind.PartialKeyword));
        private static string TypeName(ITypeSymbol type) => type.ToDisplayString(Fqn);
        private static string Parameters(IEnumerable<IParameterSymbol> parameters) => string.Join(", ", parameters.Select(p => TypeName(p.Type) + " " + Id(p.Name)));
        private static string Id(string value) => SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(value) != SyntaxKind.None ? "@" + value : value;
        private static string Lit(string value) => "\"" + (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        private static string SafeName(string value)
        {
            string safe = new string((value ?? "").Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
            if (safe.Length == 0) return "_Assembly";
            if (!char.IsLetter(safe[0]) && safe[0] != '_') safe = "_" + safe;
            return safe;
        }
        private static void BeginNamespace(StringBuilder s, INamespaceSymbol ns) { if (!ns.IsGlobalNamespace) s.Append("namespace ").Append(ns.ToDisplayString()).AppendLine("\n{"); }
        private static void EndNamespace(StringBuilder s, INamespaceSymbol ns) { if (!ns.IsGlobalNamespace) s.AppendLine("}"); }
        private static void Report(GeneratorExecutionContext c, DiagnosticDescriptor d, ISymbol symbol, params object[] args)
        { c.ReportDiagnostic(Diagnostic.Create(d, symbol.Locations.FirstOrDefault(), args)); }
    }
}
