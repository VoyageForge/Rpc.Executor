// 这是独立 CodeGen 项目的入口文件，不属于 Unity Runtime asmdef。
// dotnet/Visual Studio 编译本项目得到 Rpc.Executor.CodeGen.dll；Unity 只加载这份 DLL。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace VoyageForge.Rpc.CodeGen
{
    /// <summary>
    /// RPC 源代码生成器：扫描 [RpcService] 契约，生成方法 ID、描述符表、重载解析器和 Slot switch 分发器。
    /// </summary>
    /// <remarks>
    /// Unity 2022.3 的 Roslyn 兼容基线为 3.8，因此这里采用 ISourceGenerator + ISyntaxReceiver。
    /// 生成器在编译期间运行；最终输出是普通 C# 源码，编译后的游戏不需要加载 Microsoft.CodeAnalysis。
    /// </remarks>
    [Generator]
    public sealed class LegacyRpcGenerator : ISourceGenerator
    {
        /// <summary>必须与 Runtime RpcServiceAttribute 的完整命名空间/类型名一致。</summary>
        private const string RpcServiceAttributeName = "VoyageForge.Rpc.Executor.RpcServiceAttribute";
        /// <summary>取消令牌属于框架参数，不计入请求参数和 MethodId。</summary>
        private const string CancellationTokenName = "System.Threading.CancellationToken";

        /// <summary>RPC001：方法规范签名重复或 64 位 MethodId 发生哈希碰撞。</summary>
        private static readonly DiagnosticDescriptor MethodIdCollision = new DiagnosticDescriptor(
            "RPC001", "RPC 方法 ID 冲突", "检测到重复 RPC 方法签名或 MethodId 冲突：{0}",
            "VoyageForge.RPC", DiagnosticSeverity.Error, true,
            "同一个规范化 RPC key 只能出现一次；64 位哈希碰撞必须在编译期阻止。");

        /// <summary>RPC002：方法使用了当前版本不支持的语言签名。</summary>
        private static readonly DiagnosticDescriptor UnsupportedSignature = new DiagnosticDescriptor(
            "RPC002", "RPC 方法签名不受支持", "RPC 方法“{0}”使用了当前版本不支持的签名：{1}",
            "VoyageForge.RPC", DiagnosticSeverity.Error, true,
            "0.0.4 支持同步方法、void、Task 和 Task<T>；不支持开放泛型、ref/out/in 和 ValueTask。");

        /// <summary>RPC004：契约返回值没有用 RpcResponse 包裹。</summary>
    private static readonly DiagnosticDescriptor UnwrappedReturnType = new DiagnosticDescriptor(
        "RPC004", "RPC 返回值未用 RpcResponse 包裹", "RPC 方法“{0}”的返回值必须是 RpcResponse<T> 或 Task<RpcResponse<T>>/RpcResponse/Task/void：{1}",
        "VoyageForge.Rpc.Generator", DiagnosticSeverity.Error, true);

    /// <summary>RPC003：服务声明不是有效的非泛型 class/interface，或服务名为空。</summary>
        private static readonly DiagnosticDescriptor InvalidService = new DiagnosticDescriptor(
            "RPC003", "RPC 服务声明无效", "RPC 服务声明无效：{0}",
            "VoyageForge.RPC", DiagnosticSeverity.Error, true,
            "请在非泛型 interface/class 上声明 [RpcService(\"name\")]。");

        /// <summary>
        /// Initialize 是 Roslyn 调用的准备阶段。我们告诉编译器：请创建 RpcSyntaxReceiver 收集类型声明候选。
        /// 这里只登记接收器，不在初始化时读取项目文件，也不执行生成工作。
        /// </summary>
        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new RpcSyntaxReceiver());
        }

        /// <summary>
        /// Execute 是主要生成阶段。接收器先收集语法节点，本方法再取得语义符号、校验方法并输出代码。
        /// </summary>
        public void Execute(GeneratorExecutionContext context)
        {
            RpcSyntaxReceiver receiver = context.SyntaxReceiver as RpcSyntaxReceiver;
            if (receiver == null || receiver.TypeDeclarations.Count == 0) return;

            // partial class/interface 会出现多个语法节点，所以使用完全限定类型名做一次去重。
            Dictionary<string, INamedTypeSymbol> contracts = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
            foreach (TypeDeclarationSyntax syntax in receiver.TypeDeclarations)
            {
                // Syntax 是源码形状；SemanticModel 会告诉我们编译器最终把它解析成哪个实际类型。
                SemanticModel model = context.Compilation.GetSemanticModel(syntax.SyntaxTree);
                INamedTypeSymbol symbol = model.GetDeclaredSymbol(syntax) as INamedTypeSymbol;
                if (symbol == null || !HasAttribute(symbol, RpcServiceAttributeName)) continue;

                string typeIdentity = GetTypeIdentity(symbol);
                if (!contracts.ContainsKey(typeIdentity)) contracts.Add(typeIdentity, symbol);
            }

            if (contracts.Count == 0) return;

            bool invalidDeclaration;
            List<ServiceMethod> methods = CollectMethods(context, contracts.Values, out invalidDeclaration);
            if (invalidDeclaration) return;

            // 先检测所有 ID 冲突；存在冲突时不输出半成品 dispatcher。
            if (ReportMethodIdCollisions(context, methods)) return;

            EmitDispatcher(context, methods);
        }

        /// <summary>
        /// 语法接收器只收集可能作为服务契约的 class/interface。
        /// 不在这里搜索 [RpcService] 文本，是为了避免 using alias、global:: 和同名特性造成误判。
        /// </summary>
        private sealed class RpcSyntaxReceiver : ISyntaxReceiver
        {
            /// <summary>本次 Compilation 中遇到的类型声明候选；其中大部分普通类型会在 Execute 被过滤。</summary>
            public readonly List<TypeDeclarationSyntax> TypeDeclarations = new List<TypeDeclarationSyntax>();

            /// <summary>Roslyn 每访问一个语法节点就回调一次；这里只收集 class/interface/record 的类型声明。</summary>
            public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
            {
                TypeDeclarationSyntax declaration = syntaxNode as TypeDeclarationSyntax;
                if (declaration != null) TypeDeclarations.Add(declaration);
            }
        }

        /// <summary>生成器内部的一条服务方法记录；它不是 Runtime API，只为后续生成代码准备数据。</summary>
        private sealed class ServiceMethod
        {
            /// <summary>服务契约类型；最终 switch 将 object instance 转换为此接口/类后直接调用方法。</summary>
            public INamedTypeSymbol ContractType;
            /// <summary>Roslyn 解析后的方法符号，包含参数、返回值、可见性等真实语义信息。</summary>
            public IMethodSymbol Method;
            /// <summary>最终注册表使用的服务名。</summary>
            public string ServiceName;
            /// <summary>MethodId 的规范化输入 key。</summary>
            public string CanonicalKey;

        /// <summary>RPC 方法名（[RpcMethod] 优先，否则方法名）。</summary>
        public string RpcMethodName;
            /// <summary>FNV-1a 64 位稳定 ID。</summary>
            public ulong MethodId;
            /// <summary>switch 分发编号；仅在当前生成程序集内使用。</summary>
            public int Slot;
            /// <summary>不包含 CancellationToken 的请求参数数量。</summary>
            public int RawArgumentCount;
            /// <summary>是否返回 Task 或 Task&lt;T&gt;。</summary>
            public bool IsAsync;
            /// <summary>实际业务返回类型；无返回值使用 void。</summary>
            /// <summary>方法所需权限（[RpcAuthorize]），空表示不校验。</summary>
        public string RequiredPermission;

        public ITypeSymbol ResultType;
        }

        /// <summary>
        /// 从服务契约符号中收集方法并执行第一轮校验。
        /// 一个方法一旦签名不受支持，就报告精确位置并停止输出，防止生成代码继续产生级联错误。
        /// </summary>
        private static List<ServiceMethod> CollectMethods(
            GeneratorExecutionContext context,
            IEnumerable<INamedTypeSymbol> contractTypes,
            out bool hasError)
        {
            List<ServiceMethod> methods = new List<ServiceMethod>();
            hasError = false;

            foreach (INamedTypeSymbol contract in contractTypes)
            {
                // 服务契约只接受 class/interface。开放泛型类型的闭合参数在运行时未知，因此不生成。
                if ((contract.TypeKind != TypeKind.Interface && contract.TypeKind != TypeKind.Class) || contract.IsGenericType)
                {
                    Report(context, InvalidService, contract.Locations.FirstOrDefault(), GetTypeIdentity(contract));
                    hasError = true;
                    continue;
                }

                string serviceName = GetServiceName(contract);
                if (string.IsNullOrWhiteSpace(serviceName))
                {
                    Report(context, InvalidService, contract.Locations.FirstOrDefault(), GetTypeIdentity(contract) + "（服务名为空）");
                    hasError = true;
                    continue;
                }

                // 0.0.4 只收集契约自身声明的 public instance 方法，不自动展开继承接口。
                // 这样哪些 API 被 RPC 暴露是显式的，也避免误把基础接口的辅助成员加入协议。
                IMethodSymbol[] declaredMethods = contract.GetMembers().OfType<IMethodSymbol>()
                    .Where(method => method.MethodKind == MethodKind.Ordinary &&
                                     method.DeclaredAccessibility == Accessibility.Public)
                    .OrderBy(method => method.Name, StringComparer.Ordinal)
                    .ThenBy(method => BuildMethodSignatureForSort(method), StringComparer.Ordinal)
                    .ToArray();

                foreach (IMethodSymbol method in declaredMethods)
                {
                    string reason = GetUnsupportedReason(method);
                    if (reason != null)
                    {
                        Report(context, UnsupportedSignature, method.Locations.FirstOrDefault(), method.Name, reason);
                        hasError = true;
                        continue;
                    }

                    bool isAsync;
                    ITypeSymbol resultType;
                    ResolveReturnType(context.Compilation, method.ReturnType, out isAsync, out resultType);

                    string key = BuildCanonicalKey(serviceName, method);
                    methods.Add(new ServiceMethod
                    {
                        ContractType = contract,
                        Method = method,
                        ServiceName = serviceName,
            RpcMethodName = GetRpcMethodName(method),
                        CanonicalKey = key,
                        MethodId = Fnv1A64(key),
                        Slot = methods.Count,
                        RawArgumentCount = method.Parameters.Count(parameter => !IsCancellationToken(parameter.Type)),
                        RequiredPermission = GetRequiredPermission(context.Compilation, method),
                        IsAsync = isAsync,
                        ResultType = resultType
                    });
                }
            }
            return methods;
        }

        /// <summary>为 Slot 排序构造不含返回值的签名，避免重载在不同编译时出现不稳定顺序。</summary>
        private static string BuildMethodSignatureForSort(IMethodSymbol method)
        {
            return method.Name + "(" + string.Join(",", method.Parameters.Select(parameter => TypeIdentity(parameter.Type))) + ")";
        }

        /// <summary>检查当前版本的公开方法签名约束，并返回不支持原因；支持时返回 null。</summary>
        private static string GetUnsupportedReason(IMethodSymbol method)
        {
            if (method.IsStatic) return "不支持 static 方法，请使用实例服务方法。";
            if (method.IsGenericMethod) return "不支持泛型方法。";
            if (method.ReturnsByRef || method.ReturnsByRefReadonly) return "不支持 ref return。";
            if (method.IsAsync && method.ReturnsVoid) return "不支持 async void，请改为 Task。";

            foreach (IParameterSymbol parameter in method.Parameters)
            {
                if (parameter.RefKind != RefKind.None) return "不支持 ref/out/in 参数：" + parameter.Name;
                if (parameter.Type.TypeKind == TypeKind.Pointer) return "不支持指针参数：" + parameter.Name;
            }

            if (method.Parameters.Count(parameter => IsCancellationToken(parameter.Type)) > 1)
                return "一个方法最多只能声明一个 CancellationToken。";

            INamedTypeSymbol returnNamed = method.ReturnType as INamedTypeSymbol;
            if (returnNamed != null && returnNamed.Name == "ValueTask" &&
                returnNamed.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks")
                return "当前版本只支持 Task / Task<T>，不支持 ValueTask。";

            return null;
        }

        /// <summary>识别框架控制参数 CancellationToken。</summary>
        private static bool IsCancellationToken(ITypeSymbol type)
        {
            return type.ToDisplayString() == CancellationTokenName;
        }

        /// <summary>把返回类型归一化为 IsAsync + ResultType，便于生成器只维护一种调用结果模型。</summary>
        private static void ResolveReturnType(Compilation compilation, ITypeSymbol returnType,
            out bool isAsync, out ITypeSymbol resultType)
        {
            INamedTypeSymbol named = returnType as INamedTypeSymbol;
            if (named != null && named.Name == "Task" &&
                named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks")
            {
                isAsync = true;
                // Task 没有泛型参数，业务结果是 void；Task<T> 的结果就是 T。
                resultType = named.TypeArguments.Length == 1
                    ? named.TypeArguments[0]
                    : compilation.GetSpecialType(SpecialType.System_Void);
                return;
            }
            isAsync = false;
            resultType = returnType;
        }

        /// <summary>检查规范 key 重复以及 FNV 64 位哈希实际碰撞；true 表示已经报告错误。</summary>
        private static bool ReportMethodIdCollisions(GeneratorExecutionContext context, List<ServiceMethod> methods)
        {
            bool hasCollision = false;

            foreach (IGrouping<string, ServiceMethod> group in methods.GroupBy(item => item.CanonicalKey, StringComparer.Ordinal))
            {
                if (group.Count() <= 1) continue;
                hasCollision = true;
                foreach (ServiceMethod item in group)
                    Report(context, MethodIdCollision, item.Method.Locations.FirstOrDefault(), group.Key);
            }

            // 不同 key 理论上仍可能有相同 64 位哈希，不能只检查字符串 key 是否相同。
            foreach (IGrouping<ulong, ServiceMethod> group in methods.GroupBy(item => item.MethodId))
            {
                if (group.Select(item => item.CanonicalKey).Distinct(StringComparer.Ordinal).Count() <= 1) continue;
                hasCollision = true;
                foreach (ServiceMethod item in group)
                    Report(context, MethodIdCollision, item.Method.Locations.FirstOrDefault(), "FNV-1a 64 位碰撞：" + group.Key.ToString("X16"));
            }

            return hasCollision;
        }

        /// <summary>
        /// 输出完整的 RpcGeneratedDispatcher.g.cs。代码包括 ID 常量、描述符数组、重载解析和 Slot switch。
        /// 注意：这里生成的是 C# 源码文本，不是在编译期实例化对象或创建动态程序集。
        /// </summary>
        private static void EmitDispatcher(GeneratorExecutionContext context, List<ServiceMethod> methods)
        {
            StringBuilder source = new StringBuilder(16000);
            source.AppendLine("// <auto-generated />")
                .AppendLine("// 本文件由 RpcGenerator 生成。请修改带 [RpcService] 的契约，不要手工编辑此文件。")
                .AppendLine("#pragma warning disable CS1998")
                .AppendLine("using System;")
                .AppendLine("using System.Threading;")
                .AppendLine("using System.Threading.Tasks;")
                .AppendLine("using VoyageForge.Rpc.Executor.Abstractions;")
                .AppendLine("using VoyageForge.Rpc.Transport;")
                .AppendLine("namespace VoyageForge.Rpc.Generated")
                .AppendLine("{")
                .AppendLine("    /// <summary>自动生成的稳定 RPC 方法 ID 常量。</summary>")
                .AppendLine("    public static class RpcMethodIds")
                .AppendLine("    {");

            foreach (ServiceMethod method in methods)
            {
                source.Append("        /// <summary>").Append(XmlEscape(method.ServiceName + "." + method.Method.Name)).AppendLine("</summary>")
                    .Append("        public const ulong Method_").Append(method.Slot)
                    .Append(" = 0x").Append(method.MethodId.ToString("X16")).AppendLine("UL;");
            }

            source.AppendLine("    }");
            source.AppendLine("    /// <summary>MethodId → 可读名称（仅诊断/Debug 模式使用，不参与网络传输）。</summary>")
                .AppendLine("    public static class RpcMethodNames")
                .AppendLine("    {")
                .AppendLine("        private static readonly global::System.Collections.Generic.Dictionary<ulong, string> s_names = new global::System.Collections.Generic.Dictionary<ulong, string>()")
                .AppendLine("        {");

            foreach (ServiceMethod method in methods)
            {
                string name = method.ServiceName + "." + method.Method.Name;
                source.Append("            { RpcMethodIds.Method_").Append(method.Slot)
                    .Append(", \"").Append(CSharpEscape(name)).AppendLine("\" },");
            }

            source.AppendLine("        };")
                .AppendLine("        /// <summary>按 MethodId 查可读名称；未知返回 null。</summary>")
                .AppendLine("        public static string TryGetName(ulong methodId)")
                .AppendLine("        {")
                .AppendLine("            string name;")
                .AppendLine("            return s_names.TryGetValue(methodId, out name) ? name : null;")
                .AppendLine("        }")
                .AppendLine("    }")
                .AppendLine("    /// <summary>编译期生成的 RPC 方法解析和调用分发器。</summary>")
                .AppendLine("    public sealed class RpcGeneratedDispatcher : global::VoyageForge.Rpc.Executor.IGeneratedRpcDispatcher")
                .AppendLine("    {")
                .AppendLine("        // 描述符在静态初始化时创建；运行时不扫描类型，也不查找 MethodInfo。")
                .AppendLine("        private static readonly global::VoyageForge.Rpc.Executor.Abstractions.MethodDescriptor[] s_methods = new global::VoyageForge.Rpc.Executor.Abstractions.MethodDescriptor[]")
                .AppendLine("        {");

            foreach (ServiceMethod method in methods) EmitDescriptor(source, method);

            source.AppendLine("        };")
                .AppendLine("        /// <summary>按服务名、方法名、参数数量和可转换性选择唯一重载。</summary>")
                .AppendLine("        public bool TryResolve(string serviceName, string methodName, object[] args, out global::VoyageForge.Rpc.Executor.Abstractions.MethodDescriptor descriptor)")
                .AppendLine("        {")
                .AppendLine("            descriptor = null;")
                .AppendLine("            object[] raw = args ?? global::System.Array.Empty<object>();")
                .AppendLine("            int bestScore = -1;")
                .AppendLine("            bool ambiguous = false;")
                .AppendLine("            for (int i = 0; i < s_methods.Length; i++)")
                .AppendLine("            {")
                .AppendLine("                global::VoyageForge.Rpc.Executor.Abstractions.MethodDescriptor candidate = s_methods[i];")
                .AppendLine("                if (!global::System.String.Equals(candidate.ServiceName, serviceName, global::System.StringComparison.Ordinal) ||")
                .AppendLine("                    !global::System.String.Equals(candidate.MethodName, methodName, global::System.StringComparison.Ordinal)) continue;")
                .AppendLine("                if (raw.Length > 0 && GetRawArgumentCount(candidate.ParameterTypes) != raw.Length) continue;")
                .AppendLine("                if (raw.Length == 0) { descriptor = candidate; return true; }")
                .AppendLine("                int score = GetCandidateScore(raw, candidate.ParameterTypes);")
                .AppendLine("                if (score < 0) continue;")
                .AppendLine("                if (score > bestScore) { descriptor = candidate; bestScore = score; ambiguous = false; }")
                .AppendLine("                else if (score == bestScore) { ambiguous = true; }")
                .AppendLine("            }")
                .AppendLine("            if (ambiguous) throw new global::VoyageForge.Rpc.Executor.RpcMethodAmbiguousException(serviceName, methodName);")
                .AppendLine("            return descriptor != null;")
                .AppendLine("        }")
                                .AppendLine("        /// <summary>Mirror 式：按 MethodId 直接命中（switch），再校验参数数量与可转换性。</summary>")
                .AppendLine("        public bool TryResolve(ulong methodId, object[] args, out global::VoyageForge.Rpc.Executor.Abstractions.MethodDescriptor descriptor)")
                .AppendLine("        {")
                .AppendLine("            descriptor = null;")
                .AppendLine("            object[] raw = args ?? global::System.Array.Empty<object>();")
                .AppendLine("            switch (methodId)")
                .AppendLine("            {");

            foreach (ServiceMethod method in methods)
            {
                source.AppendLine("                case RpcMethodIds.Method_" + method.Slot + ":")
                    .AppendLine("                {")
                    .AppendLine("                    global::VoyageForge.Rpc.Executor.Abstractions.MethodDescriptor candidate = s_methods[" + method.Slot + "];")
                    .AppendLine("                    if (candidate.MethodId != methodId) return false;")
                    .AppendLine("                    if (GetRawArgumentCount(candidate.ParameterTypes) != raw.Length) return false;")
                .AppendLine("                    if (raw.Length == 0) { descriptor = candidate; return true; }")
                    .AppendLine("                    if (GetCandidateScore(raw, candidate.ParameterTypes) < 0) return false;")
                    .AppendLine("                    descriptor = candidate;")
                    .AppendLine("                    return true;")
                    .AppendLine("                }");
            }

            source.AppendLine("                default: return false;")
                .AppendLine("            }")
                .AppendLine("        }")
                .AppendLine("        // 描述符含 CancellationToken，但请求数组没有它；计算真实位置参数个数时要排除。")
                .AppendLine("        private static int GetRawArgumentCount(global::System.Type[] parameterTypes)")
                .AppendLine("        {")
                .AppendLine("            int count = 0;")
                .AppendLine("            for (int i = 0; i < parameterTypes.Length; i++) if (parameterTypes[i] != typeof(global::System.Threading.CancellationToken)) count++;")
                .AppendLine("            return count;")
                .AppendLine("        }")
                .AppendLine("        // 每个参数的精确类型匹配得分最高；基础类型转换较低；并列最优即判为歧义。")
                .AppendLine("        private static int GetCandidateScore(object[] raw, global::System.Type[] parameterTypes)")
                .AppendLine("        {")
                .AppendLine("            int rawIndex = 0; int total = 0;")
                .AppendLine("            for (int i = 0; i < parameterTypes.Length; i++)")
                .AppendLine("            {")
                .AppendLine("                global::System.Type target = parameterTypes[i];")
                .AppendLine("                if (target == typeof(global::System.Threading.CancellationToken)) continue;")
                .AppendLine("                int score = GetValueScore(raw[rawIndex++], target);")
                .AppendLine("                if (score < 0) return -1;")
                .AppendLine("                total += score;")
                .AppendLine("            }")
                .AppendLine("            return total;")
                .AppendLine("        }")
                .AppendLine("        private static int GetValueScore(object value, global::System.Type targetType)")
                .AppendLine("        {")
                .AppendLine("            global::System.Type nullable = global::System.Nullable.GetUnderlyingType(targetType);")
                .AppendLine("            global::System.Type actual = nullable ?? targetType;")
                .AppendLine("            if (value == null) return (!actual.IsValueType || nullable != null) ? 2 : -1;")
                .AppendLine("            if (actual.IsInstanceOfType(value)) return 4;")
                .AppendLine("            bool isJsonElement = value.GetType().FullName == \"System.Text.Json.JsonElement\";")
                .AppendLine("            if (actual.IsEnum) return (value is string || value is global::System.IConvertible || isJsonElement) ? 1 : -1;")
                .AppendLine("            if (actual == typeof(global::System.Guid) || actual == typeof(global::System.TimeSpan)) return (value is string || isJsonElement) ? 1 : -1;")
                .AppendLine("            if (isJsonElement) return 1; // 最终转换由 Runtime Converter 决定是否支持该值。")
                .AppendLine("            if (typeof(global::System.IConvertible).IsAssignableFrom(actual) && value is global::System.IConvertible) return 2;")
                .AppendLine("            return -1;")
                .AppendLine("        }")
                .AppendLine("        /// <summary>根据 Slot 执行编译期写出的强类型调用。</summary>")
                .AppendLine("        public async global::System.Threading.Tasks.Task<byte[]> DispatchAsync(int slot, object instance, byte[] payload, global::System.Threading.CancellationToken ct)")
                .AppendLine("        {")
                .AppendLine("            switch (slot)")
                .AppendLine("            {");

            foreach (ServiceMethod method in methods) EmitDispatchCase(source, method);

            source.AppendLine("                default: throw new global::System.ArgumentOutOfRangeException(nameof(slot), slot, \"未知 RPC Slot。\");")
                .AppendLine("            }")
                .AppendLine("        }")
                .AppendLine("    }")
                .AppendLine("}");

            context.AddSource("RpcGeneratedDispatcher.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
            EmitClients(context, methods);
            // 不再产出 Invoke{Method}（原 EmitControllerPartials）：
            //   1) 分发已经走 RpcGeneratedDispatcher 的直接强类型调用，这段产物没有任何调用方；
            //   2) 它假设"契约类名 + Controller"是目标类名，与 0.3.0 里 [RpcService] 直接标在
            //      controller 类上的写法不符，会生成引用不存在方法的无效代码（CS0103）；
            //   3) controller 常被嵌在非 partial 的容器类里，partial 扩展本身就做不到（CS0260）。
            // 保留方法体以便将来需要时恢复，但当前不调用它。
            // EmitControllerPartials(context, methods);

        }

        /// <summary>
        /// 生成调用端代理：ServicesMap 扩展方法，参数顺序与契约方法一致。
        /// 0 参数不传 payload；1 参数直接传；2+ 参数组合为 ValueTuple（与 dispatch 的反序列化对应）。
        /// </summary>
        private static void EmitClients(GeneratorExecutionContext context, List<ServiceMethod> methods)
        {
            StringBuilder source = new StringBuilder(16000);
            source.AppendLine("// <auto-generated />");
            source.AppendLine("// 由 RpcGenerator 生成：ServicesMap 调用端代理（参数顺序与契约一致）。");
            source.AppendLine("#pragma warning disable");
            source.AppendLine("namespace VoyageForge.Rpc.Generated");
            source.AppendLine("{");

            foreach (IGrouping<string, ServiceMethod> group in methods.GroupBy(item => item.ContractType.ToDisplayString()))
            {
                string contractName = group.First().ContractType.Name;
                source.Append("    /// <summary>").Append(XmlEscape(contractName)).AppendLine(" 的调用端代理。</summary>");
                source.Append("    public static class ").Append(contractName).AppendLine("Client");
                source.AppendLine("    {");

                foreach (ServiceMethod method in group)
                {
                    List<IParameterSymbol> rawParameters = method.Method.Parameters
                        .Where(parameter => !IsCancellationToken(parameter.Type)).ToList();
                    ITypeSymbol businessType = ExtractBusinessType(method.ResultType);
                    string resultType = businessType != null ? TypeSyntax(businessType) : "object";
                    string returnType = businessType != null
                        ? "global::VoyageForge.Rpc.Transport.RpcResponse<" + resultType + ">"
                        : "global::VoyageForge.Rpc.Transport.RpcResponse<object>";

                    source.Append("        /// <summary>").Append(XmlEscape(method.ServiceName + "." + method.Method.Name)).AppendLine("</summary>");
                    source.Append("        public static global::System.Threading.Tasks.Task<").Append(returnType)
                        .Append("> ").Append(method.Method.Name)
                        .Append("(this global::VoyageForge.Rpc.Executor.ServicesMap api");
                    foreach (IParameterSymbol parameter in rawParameters)
                    {
                        source.Append(", ").Append(TypeSyntax(parameter.Type)).Append(' ').Append(parameter.Name);
                    }
                    source.AppendLine(")");
                    source.AppendLine("        {");

                    string callResponse = "global::VoyageForge.Rpc.Transport.RpcResponse";
                    if (rawParameters.Count == 0)
                    {
                        source.Append("            return api.CallAsync<").Append(resultType).Append(">(\"")
                            .Append(CSharpEscape(method.ServiceName)).Append("\", \"").Append(CSharpEscape(method.RpcMethodName))
                            .AppendLine("\");");
                    }
                    else if (rawParameters.Count == 1)
                    {
                        // 1 参数：直接 Pack 参数类型（值类型泛型，无装箱）。
                        source.Append("            byte[] payload = global::VoyageForge.Rpc.Transport.RpcPayload.Pack(")
                            .Append(rawParameters[0].Name).AppendLine(");");
                        source.Append("            return api.CallAsync<").Append(resultType).Append(">(\"")
                            .Append(CSharpEscape(method.ServiceName)).Append("\", \"").Append(CSharpEscape(method.RpcMethodName))
                            .AppendLine("\", payload);");
                    }
                    else
                    {
                        // 2+ 参数：Pack 元组（值类型泛型，无装箱）。
                        source.Append("            byte[] payload = global::VoyageForge.Rpc.Transport.RpcPayload.Pack((")
                            .Append(string.Join(", ", rawParameters.Select(parameter => parameter.Name)))
                            .AppendLine("));");
                        source.Append("            return api.CallAsync<").Append(resultType).Append(">(\"")
                            .Append(CSharpEscape(method.ServiceName)).Append("\", \"").Append(CSharpEscape(method.RpcMethodName))
                            .AppendLine("\", payload);");
                    }

                    source.AppendLine("        }");
                }

                source.AppendLine("    }");
            }

            source.AppendLine("}");
            context.AddSource("RpcGeneratedClients.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
        }

        /// <summary>
        /// 为继承 RpcControllerBase 的控制器生成 Invoke{Method}(byte[] payload) 函数（partial 类中可直接调用）。
        /// </summary>
        private static void EmitControllerPartials(GeneratorExecutionContext context, List<ServiceMethod> methods)
        {
            StringBuilder source = new StringBuilder(12000);
            source.AppendLine("// <auto-generated />");
            source.AppendLine("// 由 RpcGenerator 生成：RpcControllerBase 派生类的 Invoke{Method} 函数。");
            source.AppendLine("#pragma warning disable");
            source.AppendLine("namespace VoyageForge.Rpc.Generated");
            source.AppendLine("{");

            foreach (IGrouping<string, ServiceMethod> group in methods.GroupBy(item => item.ContractType.ToDisplayString()))
            {
                ITypeSymbol contract = group.First().ContractType;
                INamedTypeSymbol named = contract as INamedTypeSymbol;
                if (named == null || named.TypeKind != TypeKind.Class) continue;

                // 只处理顶层类型。
                // 嵌套类型要给外层补一层 partial 外壳才能扩展，而外层（例如示例里的
                // ServicesMapSample 静态容器类）往往不是 partial —— 硬生成会导致 CS0260
                // "缺少 partial 修饰符"。这些 controller 的 Invoke{Method} 目前没有调用方
                // （分发走 RpcGeneratedDispatcher 的直接强类型调用），所以跳过是安全的。
                if (named.ContainingType != null) continue;

                string contractNamespace = named.ContainingNamespace.IsGlobalNamespace
                    ? null
                    : named.ContainingNamespace.ToDisplayString();
                if (contractNamespace != null)
                {
                    source.Append("    namespace ").AppendLine(contractNamespace);
                    source.AppendLine("    {");
                }

                // 注意：契约类本身就是 controller（[RpcService] 标在 controller 上），
                // 所以这里直接用类名生成 partial 扩展，不能再拼 "Controller" 后缀
                // —— 那会得到一个不存在的 UserControllerController，里面任何方法调用都找不到。
                source.Append("    public partial class ").Append(named.Name).AppendLine();
                source.AppendLine("    {");
                foreach (ServiceMethod method in group)
                {
                    List<IParameterSymbol> rawParameters = method.Method.Parameters
                        .Where(parameter => !IsCancellationToken(parameter.Type)).ToList();
                    source.Append("        /// <summary>").Append(XmlEscape(method.RpcMethodName)).AppendLine("。</summary>");
                    source.Append("        internal global::System.Threading.Tasks.Task<byte[]> Invoke")
                        .Append(method.Method.Name)
                        .AppendLine("(byte[] payload, global::System.Threading.CancellationToken ct)");
                    source.AppendLine("        {");
                    if (rawParameters.Count >= 2)
                    {
                        source.Append("            var parameters = global::VoyageForge.Rpc.Transport.RpcPayload.Unpack<")
                            .Append(TupleType(rawParameters)).AppendLine(">(payload);");
                    }

                    List<string> arguments = new List<string>();
                    for (int index = 0; index < rawParameters.Count; index++)
                    {
                        arguments.Add(rawParameters.Count == 1
                            ? "global::VoyageForge.Rpc.Transport.RpcPayload.Unpack<" + TypeSyntax(rawParameters[0].Type) + ">(payload)"
                            : "parameters." + rawParameters[index].Name);
                    }

                    if (method.Method.Parameters.Any(parameter => IsCancellationToken(parameter.Type)))
                    {
                        int tokenIndex = method.Method.Parameters.ToList().FindIndex(parameter => IsCancellationToken(parameter.Type));
                        if (tokenIndex < arguments.Count) arguments.Insert(tokenIndex, "ct");
                        else arguments.Add("ct");
                    }

                    string call = method.Method.Name + "(" + string.Join(", ", arguments) + ")";
                    if (method.Method.ReturnsVoid)
                    {
                        source.Append("            ").Append(call).AppendLine(";");
                        source.AppendLine("            return global::System.Threading.Tasks.Task.FromResult(global::System.Array.Empty<byte>());");
                    }
                    else if (IsNonGenericTask(method.Method.ReturnType))
                    {
                        source.Append("            await ").Append(call).AppendLine(".ConfigureAwait(false);");
                        source.AppendLine("            return global::System.Array.Empty<byte>();");
                    }
                    else if (IsGenericTask(method.Method.ReturnType))
                    {
                        source.Append("            var response = await ").Append(call).AppendLine(".ConfigureAwait(false);");
                        source.AppendLine("            if (response != null && !response.Success && response.Error == null) response.Success = true;");
                        source.AppendLine("            return global::VoyageForge.Rpc.Transport.RpcPayload.Pack(response.Data);");
                    }
                    else
                    {
                        source.Append("            var response = ").Append(call).AppendLine(";");
                        source.AppendLine("            if (response != null && !response.Success && response.Error == null) response.Success = true;");
                        source.AppendLine("            return global::System.Threading.Tasks.Task.FromResult(global::VoyageForge.Rpc.Transport.RpcPayload.Pack(response.Data));");
                    }

                    source.AppendLine("        }");
                }

                source.AppendLine("    }");
                if (contractNamespace != null) source.AppendLine("    }");
            }

            source.AppendLine("}");
            context.AddSource("RpcControllerInvoke.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
        }


        /// <summary>把参数列表转成 C# ValueTuple 类型文本：({T0} {n0}, {T1} {n1}, ...)。</summary>
        private static string TupleType(List<IParameterSymbol> parameters)
        {
            StringBuilder builder = new StringBuilder("(");
            for (int index = 0; index < parameters.Count; index++)
            {
                if (index > 0) builder.Append(", ");
                builder.Append(TypeSyntax(parameters[index].Type)).Append(' ').Append(parameters[index].Name);
            }

            builder.Append(')');
            return builder.ToString();
        }

        /// <summary>
        /// 从契约返回类型提取业务类型：RpcResponse&lt;T&gt; / Task&lt;RpcResponse&lt;T&gt;&gt; → T；
        /// RpcResponse / Task → object；其他返回类型视为错误（契约必须用 RpcResponse 包裹）。
        /// </summary>
        private static ITypeSymbol ExtractBusinessType(ITypeSymbol returnType)
        {
            ITypeSymbol current = returnType;
            if (current is INamedTypeSymbol named && named.Name == "Task" && named.TypeArguments.Length == 1)
            {
                current = named.TypeArguments[0];
            }

            if (!(current is INamedTypeSymbol responseType)) return null;
            if (responseType.Name != "RpcResponse" ||
                responseType.ContainingNamespace.ToDisplayString() != "VoyageForge.Rpc.Transport") return null;
            return responseType.TypeArguments.Length == 1 ? responseType.TypeArguments[0] : null;
        }

        /// <summary>生成一个 MethodDescriptor 数组初始化项；参数类型按实际方法声明顺序保存。</summary>
        private static void EmitDescriptor(StringBuilder source, ServiceMethod method)
        {
            source.AppendLine("            new global::VoyageForge.Rpc.Executor.Abstractions.MethodDescriptor")
                .AppendLine("            {")
                .Append("                ServiceName = \"").Append(CSharpEscape(method.ServiceName)).AppendLine("\",")
                .Append("                MethodName = \"").Append(CSharpEscape(method.RpcMethodName)).AppendLine("\",")
                .Append("                MethodId = RpcMethodIds.Method_").Append(method.Slot).AppendLine(",")
                // Slot 必须显式写入：MethodDescriptor.Slot 是普通 int 字段（默认 0），
                // 漏掉它会让所有描述符的 Slot 都变成 0，DispatchAsync 于是永远执行 case 0
                // —— 表现为"调用方法 B 却执行了方法 A"，而且编译期毫无提示。
                .Append("                Slot = ").Append(method.Slot).AppendLine(",")
                .Append("                RequiredPermission = \"").Append(CSharpEscape(method.RequiredPermission ?? "")).AppendLine("\",")
                .Append("                IsAsync = ").Append(method.IsAsync ? "true" : "false").AppendLine(",")
                .Append("                ResultType = typeof(").Append(TypeSyntax(method.ResultType)).AppendLine("),")
                .Append("                ParameterTypes = new global::System.Type[] { ")
                .Append(string.Join(", ", method.Method.Parameters.Select(parameter => "typeof(" + TypeSyntax(parameter.Type) + ")")))
                .AppendLine(" }")
                .AppendLine("            },");
        }

        /// <summary>
        /// 生成一个 switch case。参数顺序与契约方法保持一致；CancellationToken 使用 DispatchAsync 收到的 ct。
        /// </summary>
        private static void EmitDispatchCase(StringBuilder source, ServiceMethod method)
        {
            string contractType = TypeSyntax(method.ContractType);
            List<IParameterSymbol> rawParameters = new List<IParameterSymbol>();
            foreach (IParameterSymbol parameter in method.Method.Parameters)
            {
                if (!IsCancellationToken(parameter.Type)) rawParameters.Add(parameter);
            }

            List<string> arguments = new List<string>();
            if (rawParameters.Count == 1)
            {
                // 1 参数：直接反序列化参数类型（无 DTO）。
                arguments.Add("global::VoyageForge.Rpc.Transport.RpcPayload.Unpack<" + TypeSyntax(rawParameters[0].Type) + ">(payload)");
            }
            else if (rawParameters.Count >= 2)
            {
                // 2+ 参数：MemoryPack 原生 ValueTuple（无 DTO）。
                for (int index = 0; index < rawParameters.Count; index++)
                {
                    arguments.Add("parameters." + rawParameters[index].Name);
                }
            }

            // CancellationToken 原样注入（在参数序列中的实际位置插入）。
            if (method.Method.Parameters.Any(parameter => IsCancellationToken(parameter.Type)))
            {
                int tokenIndex = method.Method.Parameters.ToList().FindIndex(parameter => IsCancellationToken(parameter.Type));
                if (tokenIndex < arguments.Count) arguments.Insert(tokenIndex, "ct");
                else arguments.Add("ct");
            }

            string call = "((" + contractType + ")instance)." + method.Method.Name + "(" + string.Join(", ", arguments) + ")";
            source.Append("                case ").Append(method.Slot).AppendLine(":")
                .AppendLine("                {");

            if (rawParameters.Count >= 2)
            {
                source.Append("                    var parameters = global::VoyageForge.Rpc.Transport.RpcPayload.Unpack<")
                    .Append(TupleType(rawParameters)).AppendLine(">(payload);");
            }

            if (method.Method.ReturnsVoid)
            {
                source.Append("                    ").Append(call).AppendLine(";");
                source.AppendLine("                    return global::System.Array.Empty<byte>();");
            }
            else if (IsNonGenericTask(method.Method.ReturnType))
            {
                source.Append("                    await ").Append(call).AppendLine(".ConfigureAwait(false);");
                source.AppendLine("                    return global::System.Array.Empty<byte>();");
            }
            else if (IsGenericTask(method.Method.ReturnType))
            {
                source.Append("                    var response = await ").Append(call).AppendLine(".ConfigureAwait(false);");
                source.AppendLine("                    if (response != null && !response.Success && response.Error == null) response.Success = true;");
                source.AppendLine("                    return global::VoyageForge.Rpc.Transport.RpcPayload.Pack(response.Data);");
            }
            else
            {
                source.Append("                    var response = ").Append(call).AppendLine(";");
                source.AppendLine("                    if (response != null && !response.Success && response.Error == null) response.Success = true;");
                source.AppendLine("                    return global::VoyageForge.Rpc.Transport.RpcPayload.Pack(response.Data);");
            }

            source.AppendLine("                }");
        }

        /// <summary>方法是否标注了 [RpcMethod]（Controller 模式下必须标注才参与 RPC）。</summary>
        private static bool HasRpcMethod(IMethodSymbol method)
        {
            foreach (AttributeData attribute in method.GetAttributes())
            {
                if (attribute.AttributeClass != null &&
                    attribute.AttributeClass.ToDisplayString() == "VoyageForge.Rpc.Executor.RpcMethodAttribute")
                    return true;
            }

            return false;
        }

        /// <summary>RPC 方法名：[RpcMethod] 显式名优先，否则使用方法名。</summary>
        private static string GetRpcMethodName(IMethodSymbol method)
        {
            foreach (AttributeData attribute in method.GetAttributes())
            {
                if (attribute.AttributeClass == null) continue;
                if (attribute.AttributeClass.ToDisplayString() != "VoyageForge.Rpc.Executor.RpcMethodAttribute") continue;
                if (attribute.ConstructorArguments.Length > 0)
                {
                    string value = attribute.ConstructorArguments[0].Value as string;
                    if (!string.IsNullOrEmpty(value)) return value;
                }
            }

            return method.Name;
        }

        /// <summary>读取方法上的 [RpcAuthorize] 权限名（多个时取最后一个非空）。</summary>
        private static string GetRequiredPermission(Compilation compilation, IMethodSymbol method)
        {
            string permission = null;
            foreach (AttributeData attribute in method.GetAttributes())
            {
                if (attribute.AttributeClass == null) continue;
                if (attribute.AttributeClass.ToDisplayString() != "VoyageForge.Rpc.Executor.RpcAuthorizeAttribute") continue;
                if (attribute.ConstructorArguments.Length > 0)
                {
                    string value = attribute.ConstructorArguments[0].Value as string;
                    if (!string.IsNullOrEmpty(value)) permission = value;
                }
            }

            return permission;
        }

        /// <summary>判断返回类型是不是 System.Threading.Tasks.Task（非泛型）。</summary>
        private static bool IsNonGenericTask(ITypeSymbol type)
        {
            INamedTypeSymbol named = type as INamedTypeSymbol;
            return named != null && named.Name == "Task" && named.TypeArguments.Length == 0 &&
                   named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks";
        }

        /// <summary>判断返回类型是不是 System.Threading.Tasks.Task&lt;T&gt;。</summary>
        private static bool IsGenericTask(ITypeSymbol type)
        {
            INamedTypeSymbol named = type as INamedTypeSymbol;
            return named != null && named.Name == "Task" && named.TypeArguments.Length == 1 &&
                   named.ContainingNamespace.ToDisplayString() == "System.Threading.Tasks";
        }

        /// <summary>读取服务特性中的名称；未显式传入时使用契约完整类型名。</summary>
        private static string GetServiceName(INamedTypeSymbol contract)
        {
            AttributeData attribute = contract.GetAttributes().FirstOrDefault(item =>
                item.AttributeClass != null && item.AttributeClass.ToDisplayString() == RpcServiceAttributeName);
            if (attribute != null && attribute.ConstructorArguments.Length > 0)
            {
                string explicitName = attribute.ConstructorArguments[0].Value as string;
                if (!string.IsNullOrWhiteSpace(explicitName)) return explicitName;
            }
            return GetTypeIdentity(contract);
        }

        /// <summary>把服务/方法/参数转换成不含歧义的 key；参数类型不含 CancellationToken。</summary>
        private static string BuildCanonicalKey(string serviceName, IMethodSymbol method)
        {
            string[] argumentTypes = method.Parameters
                .Where(parameter => !IsCancellationToken(parameter.Type))
                .Select(parameter => TypeIdentity(parameter.Type)).ToArray();
            return "v1\u001f" + serviceName + "\u001f" + method.Name + "\u001f" + string.Join("\u001f", argumentTypes);
        }

        /// <summary>生成稳定类型身份名，避免 int/Int32 或不同命名空间的同名类型产生冲突。</summary>
        private static string TypeIdentity(ITypeSymbol type)
        {
            return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty);
        }

        /// <summary>生成 typeof(...) 中使用的全限定 C# 类型写法。</summary>
        private static string TypeSyntax(ITypeSymbol type)
        {
            // C# 语言要求 typeof(void)，不能依赖 System.Void 的普通完整类型拼写。
            if (type.SpecialType == SpecialType.System_Void) return "void";
            return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        /// <summary>取得不带 global:: 前缀的类型身份名。</summary>
        private static string GetTypeIdentity(INamedTypeSymbol type) { return TypeIdentity(type); }

        /// <summary>通过完整 Attribute 类型名判断服务契约是否真的使用了 RpcServiceAttribute。</summary>
        private static bool HasAttribute(INamedTypeSymbol type, string metadataName)
        {
            return type.GetAttributes().Any(attribute =>
                attribute.AttributeClass != null && attribute.AttributeClass.ToDisplayString() == metadataName);
        }

        /// <summary>报告诊断。location 指向用户声明位置，而不是生成器本身。</summary>
        private static void Report(GeneratorExecutionContext context, DiagnosticDescriptor descriptor,
            Location location, params object[] arguments)
        {
            context.ReportDiagnostic(Diagnostic.Create(descriptor, location, arguments));
        }

        /// <summary>把用户字符串转义成合法 C# 字符串字面量内部内容。</summary>
        private static string CSharpEscape(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        }

        /// <summary>把用户文字转义成合法 XML 文档注释。</summary>
        private static string XmlEscape(string value)
        {
            return (value ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        /// <summary>
        /// 固定的 FNV-1a 64 位实现：UTF-8 编码后逐字节 XOR，再乘 prime。
        /// 这里的常量、编码和 BuildCanonicalKey 必须与 Rpc.Executor Runtime 保持一致。
        /// </summary>
        private static ulong Fnv1A64(string key)
        {
            const ulong offsetBasis = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offsetBasis;
            byte[] bytes = Encoding.UTF8.GetBytes(key);
            for (int index = 0; index < bytes.Length; index++)
            {
                hash ^= bytes[index];
                hash *= prime;
            }
            return hash;
        }
    }
}
