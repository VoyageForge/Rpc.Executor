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
    /// <summary>生成调用方 ControllerMap 代理、服务端 Controller Dispatcher 和程序集级 Catalog。</summary>
    [Generator]
    public sealed class ControllerRpcGenerator : ISourceGenerator
    {
        private const string ControllerAttr = "VoyageForge.Rpc.Transport.Session.ControllerAttribute";
        private const string MapAttr = "VoyageForge.Rpc.Transport.Session.ControllerMapAttribute";
        private const string RpcControllerAttr = "VoyageForge.Rpc.Executor.RpcControllerAttribute";
        private const string ControllerBase = "VoyageForge.Rpc.Executor.RpcControllerBase";
        private const string CancellationTokenName = "System.Threading.CancellationToken";
        private static readonly SymbolDisplayFormat Fqn = SymbolDisplayFormat.FullyQualifiedFormat;
        private static readonly DiagnosticDescriptor Invalid = new DiagnosticDescriptor("RPC100", "RPC 声明无效", "RPC 声明无效：{0}", "VoyageForge.RPC", DiagnosticSeverity.Error, true);
        private static readonly DiagnosticDescriptor Unsupported = new DiagnosticDescriptor("RPC101", "RPC 方法不受支持", "RPC 方法“{0}”不受支持：{1}", "VoyageForge.RPC", DiagnosticSeverity.Error, true);
        private static readonly DiagnosticDescriptor Collision = new DiagnosticDescriptor("RPC102", "MethodId 冲突", "RPC MethodId 冲突：{0}", "VoyageForge.RPC", DiagnosticSeverity.Error, true);

        /// <summary>
        /// 临时 IO 探针（已停用，保留空实现避免改动过多调用点）。
        /// 排查结论：ControllerRpcGenerator 在样本程序集上执行正常、EmitMap 的 AddSource 成功，
        /// 但 Unity 的增量编译不会把该程序集的 analyzer 产物纳入编译（软链接目录更容易触发），
        /// 从而形成"缺生成类型 → 编译失败 → 产物被丢弃"的死循环。
        /// </summary>
        private static void ProbeIf(GeneratorExecutionContext context, string message)
        {
        }

        public void Initialize(GeneratorInitializationContext context) => context.RegisterForSyntaxNotifications(() => new Receiver());

        public void Execute(GeneratorExecutionContext context)
        {
            Receiver receiver = context.SyntaxReceiver as Receiver;
            ProbeIf(context, "Execute 进入；receiver=" + (receiver == null ? "NULL" : "types=" + receiver.Types.Count));
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
                INamedTypeSymbol contract = attr.ConstructorArguments.Length == 0 ? null : attr.ConstructorArguments[0].Value as INamedTypeSymbol;
                ControllerModel model = BuildController(context, type, contract);
                if (model != null) controllers.Add(model);
            }

            // 请求 DTO 由本生成器自动生成：需要 Compilation 来构造临时 Compilation，
            // 再驱动 MemoryPack 的 Emit() 产出序列化实现（详见 RpcDtoMemoryPackEmitter 注释）。
            // 整个 Execute 共用同一个 emitter 与去重集合。
            CSharpParseOptions dtoParseOptions = context.ParseOptions as CSharpParseOptions
                                                 ?? new CSharpParseOptions(LanguageVersion.CSharp9);
            RpcDtoMemoryPackEmitter dtoEmitter = new RpcDtoMemoryPackEmitter(
                context.Compilation, dtoParseOptions, context.CancellationToken);
            HashSet<string> generatedDtos = new HashSet<string>(StringComparer.Ordinal);

            // 顺序很重要：先产出调用端 Map 的代理，再产出服务端 Dispatcher。
            // 两者互不依赖，但 Map 的产物（ControllerMap 上的代理属性）被用户代码直接引用，
            // 一旦服务端环节抛异常中断 Execute，就会连带 Map 也生成不出来，
            // 表现为"GameControllerMap 没有 User 属性"这种离根因很远的错误。
            foreach (INamedTypeSymbol type in types.Values)
            {
                AttributeData attr = Attribute(type, MapAttr);
                ProbeIf(context, "检查类型 " + type.Name + " MapAttr=" + (attr != null));
                if (attr == null) continue;
                EmitMap(context, type, attr, dtoEmitter, generatedDtos);
            }

            if (controllers.Count > 0 && !HasCollision(context, controllers))
            {
                foreach (ControllerModel controller in controllers) EmitDispatcher(context, controller, dtoEmitter, generatedDtos);
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
            public INamedTypeSymbol Type, Contract;
            public string Name;
            public readonly List<MethodModel> Methods = new List<MethodModel>();
        }
        private sealed class MethodModel
        {
            public IMethodSymbol ContractMethod, Implementation;
            public string Name, Signature, DispatchName;
            public ulong Id;
            public List<IParameterSymbol> Args;
            public IParameterSymbol Cancellation;
            public ITypeSymbol Result;
            public bool HasResult;
            /// <summary>请求 DTO 的完整类型名；0 参数方法为 null（走空 Payload）。</summary>
            public string DtoFqn;
        }

        private static ControllerModel BuildController(GeneratorExecutionContext context, INamedTypeSymbol type, INamedTypeSymbol contract)
        {
            if (type.TypeKind != TypeKind.Class || type.IsAbstract || type.IsGenericType || type.ContainingType != null || !IsPartial(type) || !Derives(type, ControllerBase))
            { Report(context, Invalid, type, type.Name + " 必须是非抽象、非泛型、partial class 且继承 RpcControllerBase。"); return null; }
            if (contract == null || contract.TypeKind != TypeKind.Interface || contract.IsGenericType || Attribute(contract, ControllerAttr) == null ||
                !type.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, contract)))
            { Report(context, Invalid, type, type.Name + " 必须通过 [RpcController(typeof(IContract))] 指定其实现的 [Controller] 接口。"); return null; }

            IEnumerable<INamedTypeSymbol> contractTree = contract.AllInterfaces.Concat(new[] { contract });
            if (contractTree.SelectMany(i => i.GetMembers()).Any(m => m is IPropertySymbol || m is IEventSymbol))
            { Report(context, Invalid, contract, contract.Name + " 契约只允许声明 RPC 方法，不支持属性和事件。"); return null; }

            ControllerModel model = new ControllerModel { Type = type, Contract = contract, Name = ControllerName(contract) };
            foreach (IMethodSymbol method in Methods(contract))
            {
                List<IParameterSymbol> args; IParameterSymbol cancellation; ITypeSymbol result; bool hasResult; string error;
                if (!ValidateMethod(method, out args, out cancellation, out result, out hasResult, out error))
                { Report(context, Unsupported, method, method.Name, error); return null; }
                IMethodSymbol implementation = type.FindImplementationForInterfaceMember(method) as IMethodSymbol;
                if (implementation == null || implementation.DeclaredAccessibility != Accessibility.Public || implementation.IsStatic)
                { Report(context, Invalid, method, type.Name + " 必须使用 public 实例方法实现 " + method.Name + "。"); return null; }
                ulong id = Hash(CanonicalKey(model.Name, method.Name, args));
                model.Methods.Add(new MethodModel
                {
                    ContractMethod = method, Implementation = implementation, Name = method.Name,
                    Args = args, Cancellation = cancellation, Result = result, HasResult = hasResult,
                    Id = id, Signature = Signature(args), DispatchName = "__RpcDispatch_" + id.ToString("X16")
                });
            }
            return model;
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
            if (hasResult) result = task.TypeArguments[0];
            return true;
        }

        private static void EmitMap(GeneratorExecutionContext context, INamedTypeSymbol map, AttributeData attribute,
            RpcDtoMemoryPackEmitter dtoEmitter, HashSet<string> generatedDtos)
        {
            ProbeIf(context, "EmitMap 进入: " + map.Name);
            if (map.TypeKind != TypeKind.Class || map.IsGenericType || map.ContainingType != null || !IsPartial(map) || !Derives(map, "VoyageForge.Rpc.Transport.Session.ControllerMap"))
            { ProbeIf(context, "EmitMap 校验失败: " + map.Name); Report(context, Invalid, map, map.Name + " 必须是非泛型 partial class 并继承 ControllerMap。"); return; }
            List<INamedTypeSymbol> contracts = attribute.ConstructorArguments.Length == 0
                ? new List<INamedTypeSymbol>()
                : attribute.ConstructorArguments[0].Values.Select(v => v.Value as INamedTypeSymbol).Where(t => t != null).ToList();
            if (contracts.Count == 0) return;
            Dictionary<ulong, string> mapMethodIds = new Dictionary<ulong, string>();
            foreach (INamedTypeSymbol contract in contracts)
            foreach (IMethodSymbol method in Methods(contract))
            {
                List<IParameterSymbol> args; IParameterSymbol cancellation; ITypeSymbol result; bool hasResult; string error;
                if (!ValidateMethod(method, out args, out cancellation, out result, out hasResult, out error))
                { Report(context, Unsupported, method, method.Name, error); return; }
                ulong id = Hash(CanonicalKey(ControllerName(contract), method.Name, args));
                string key = ControllerName(contract) + "." + method.Name + Signature(args);
                if (mapMethodIds.TryGetValue(id, out string previous))
                { Report(context, Collision, map, previous + " 与 " + key + "（重复代理映射）"); return; }
                mapMethodIds.Add(id, key);
            }
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            StringBuilder s = new StringBuilder(); BeginNamespace(s, map.ContainingNamespace);
            s.Append("partial class ").Append(Id(map.Name)).AppendLine("\n{");
            List<string> proxies = new List<string>();
            foreach (INamedTypeSymbol contract in contracts)
            {
                if (contract.TypeKind != TypeKind.Interface || Attribute(contract, ControllerAttr) == null)
                { ProbeIf(context, "EmitMap 契约校验失败: " + contract.Name); Report(context, Invalid, map, "ControllerMap 只能包含带 [Controller] 的接口。"); return; }
                ProbeIf(context, "EmitMap 契约通过: " + contract.Name);
                string name = ControllerPropertyName(contract);
                if (!SyntaxFacts.IsValidIdentifier(name) || !names.Add(name))
                { Report(context, Invalid, map, "ControllerMap 属性名无效或重复：" + name); return; }
                if (contract.AllInterfaces.Concat(new[] { contract }).SelectMany(i => i.GetMembers()).Any(m => m is IPropertySymbol || m is IEventSymbol))
                { Report(context, Invalid, contract, contract.Name + " 契约只允许声明 RPC 方法，不支持属性和事件。"); return; }
                string proxy = "__RpcProxy_" + SafeName(contract.ToDisplayString(Fqn));
                proxies.Add(proxy);
                s.Append("    public ").Append(TypeName(contract)).Append(' ').Append(Id(name)).AppendLine(" { get; private set; }");
            }
            s.AppendLine("    protected override void InitializeGenerated()");
            s.AppendLine("    {"); s.AppendLine("        base.InitializeGenerated();");
            for (int i = 0; i < contracts.Count; i++) s.Append("        this.").Append(Id(ControllerPropertyName(contracts[i]))).Append(" = new ").Append(proxies[i]).AppendLine("(this.Session);");
            s.AppendLine("    }");
            for (int i = 0; i < contracts.Count; i++) EmitProxy(s, contracts[i], proxies[i], context, dtoEmitter, generatedDtos);
            s.AppendLine("}"); EndNamespace(s, map.ContainingNamespace);
            ProbeIf(context, "EmitMap 即将 AddSource: " + map.Name + " 长度=" + s.Length);
            context.AddSource(SafeName(map.ToDisplayString(Fqn)) + ".ControllerMap.g.cs", SourceText.From(s.ToString(), Encoding.UTF8));
            ProbeIf(context, "EmitMap AddSource 完成: " + map.Name);
        }

        private static void EmitProxy(StringBuilder s, INamedTypeSymbol contract, string proxy,
            GeneratorExecutionContext context, RpcDtoMemoryPackEmitter dtoEmitter, HashSet<string> generatedDtos)
        {
            s.Append("    private sealed class ").Append(proxy).Append(" : ").Append(TypeName(contract)).AppendLine("\n    {");
            s.AppendLine("        private readonly global::VoyageForge.Rpc.Transport.Session.TcpSessionBase _session;");
            s.Append("        public ").Append(proxy).AppendLine("(global::VoyageForge.Rpc.Transport.Session.TcpSessionBase session) { _session = session; }");
            foreach (IMethodSymbol method in Methods(contract))
            {
                List<IParameterSymbol> args; IParameterSymbol cancellation; ITypeSymbol result; bool hasResult; string error;
                if (!ValidateMethod(method, out args, out cancellation, out result, out hasResult, out error)) continue;
                ulong id = Hash(CanonicalKey(ControllerName(contract), method.Name, args));

                // 调用端同样需要 DTO —— payload 就是它。两端用同一套命名规则算出同一个全名。
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

        private static void EmitDispatcher(GeneratorExecutionContext context, ControllerModel controller,
            RpcDtoMemoryPackEmitter dtoEmitter, HashSet<string> generatedDtos)
        {
            // 先把所有请求 DTO 生成出来。任一失败就整个 controller 不产出 dispatcher ——
            // 半截的 dispatcher 会把"DTO 生成失败"这个根因埋在一堆后续编译错误里。
            foreach (MethodModel method in controller.Methods)
            {
                method.DtoFqn = RpcDtoFactory.EnsureDto(context, dtoEmitter, controller.Name, method.Name, method.Args, generatedDtos);
                if (method.Args.Count > 0 && method.DtoFqn == null) return;
            }

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
                        args.Add("__req." + RpcDtoFactory.ToPascalCase(method.Args[i].Name));
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
                s.Append("                Handler = (payload, ct) => { var instance = controllerFactory(typeof(").Append(TypeName(controller.Type)).AppendLine("));");
                s.Append("                    if (!(instance is ").Append(TypeName(controller.Type)).Append(" controller)) throw new global::System.InvalidOperationException(\"Controller factory 返回了错误类型：").Append(TypeName(controller.Type)).AppendLine("\");");
                s.Append("                    return controller.").Append(method.DispatchName).AppendLine("(payload, ct); }");
                s.AppendLine("            });");
            }
            s.AppendLine("        }\n    }\n}");
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
        /// <para>
        /// <b>为什么用命名赋值（<c>P = arg</c>）而不是按位置</b>：DTO 的属性顺序是按<b>参数名排序</b>
        /// 定的（保证两端确定性一致），与参数声明顺序不同。命名赋值让两者解耦，
        /// 也避免"改了排序规则导致赋值错位"这类静默错误。
        /// </para>
        /// <para>0 参数保持空 payload —— 没有请求体，不生成 DTO。</para>
        /// </remarks>
        private static string SerializeArgs(string dtoFqn, List<IParameterSymbol> args)
        {
            if (args.Count == 0) return "global::System.Array.Empty<byte>()";
            string assignments = string.Join(", ", args.Select(a => RpcDtoFactory.ToPascalCase(a.Name) + " = " + Id(a.Name)));
            return "global::MemoryPack.MemoryPackSerializer.Serialize(new " + dtoFqn + " { " + assignments + " })";
        }
        private static string MethodIdentity(IMethodSymbol method) => method.Name + "(" +
            string.Join(",", method.Parameters.Select(p => p.Type.ToDisplayString(Fqn))) + ")->" + method.ReturnType.ToDisplayString(Fqn);
        private static string Signature(List<IParameterSymbol> args) => "(" + string.Join(", ", args.Select(a => a.Type.ToDisplayString(Fqn))) + ")";
        private static string CanonicalKey(string name, string method, List<IParameterSymbol> args) =>
            "v2\u001F" + name + "\u001F" + method + "\u001F" + string.Join("\u001E", args.Select(a => a.Type.ToDisplayString(Fqn)));
        private static ulong Hash(string value)
        {
            const ulong offset = 14695981039346656037UL, prime = 1099511628211UL;
            ulong hash = offset; byte[] bytes = Encoding.UTF8.GetBytes(value);
            foreach (byte b in bytes) { hash ^= b; hash *= prime; } return hash;
        }
        private static string ControllerPropertyName(INamedTypeSymbol contract)
        {
            return contract.Name.StartsWith("I", StringComparison.Ordinal) && contract.Name.Length > 1
                ? contract.Name.Substring(1) : contract.Name;
        }
        private static string ControllerName(INamedTypeSymbol contract)
        {
            AttributeData a = Attribute(contract, ControllerAttr);
            string name = a != null && a.ConstructorArguments.Length > 0 ? a.ConstructorArguments[0].Value as string : null;
            return string.IsNullOrWhiteSpace(name) ? (contract.Name.StartsWith("I", StringComparison.Ordinal) && contract.Name.Length > 1 ? contract.Name.Substring(1) : contract.Name) : name;
        }
        private static AttributeData Attribute(ISymbol symbol, string fullName) => symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass != null && a.AttributeClass.ToDisplayString() == fullName);
        private static bool Derives(INamedTypeSymbol type, string fullName)
        { for (INamedTypeSymbol t = type; t != null; t = t.BaseType) if (t.ToDisplayString() == fullName) return true; return false; }
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
