using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace VoyageForge.Rpc.CodeGen
{
    /// <summary>
    /// "确保某个 RPC 方法的请求 DTO 已生成"的统一入口。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 调用方程序集（<c>[ControllerMap]</c>）和服务端程序集（<c>[RpcController]</c>）是<b>分开编译</b>的，
    /// 两边都需要同一个 DTO。这个工厂用<b>确定性的命名规则</b>保证两端算出同一个类型全名：
    /// </para>
    /// <code>
    /// VoyageForge.Rpc.Generated.Dto.{ControllerName}{MethodName}Request
    /// </code>
    /// <para>
    /// 同一程序集内如果既有 Controller 又有 Map 引用了同一个契约，会重复请求同一个 DTO ——
    /// 用 <c>generated</c> 集合去重，避免 Roslyn 报"重复 hint name"。
    /// </para>
    /// <para>
    /// <b>可读性策略</b>：属性类型写短名（<c>int</c>、<c>List&lt;UserInfo&gt;</c>），
    /// 并把用到的命名空间收集成文件头部的 <c>using</c>。
    /// 生成代码是给人看、也要给人 debug 的，全限定名会把一行属性声明撑得没法读。
    /// </para>
    /// </remarks>
    public static class RpcDtoFactory
    {
        /// <summary>请求 DTO 的命名空间（两端必须一致）。</summary>
        public const string DtoNamespace = "VoyageForge.Rpc.Generated.Dto";

        /// <summary>DTO 生成失败。</summary>
        public static readonly DiagnosticDescriptor DtoBuildFailed = new DiagnosticDescriptor(
            "RPC103",
            "RPC 请求 DTO 生成失败",
            "为 {0}.{1} 生成请求 DTO 失败：{2}",
            "VoyageForge.RPC",
            DiagnosticSeverity.Error,
            true);

        /// <summary>
        /// 确保给定方法的请求 DTO 已被生成，返回其完整类型名（失败返回 null）。
        /// </summary>
        /// <param name="context">生成器执行上下文（用于 AddSource / ReportDiagnostic）。</param>
        /// <param name="emitter">MemoryPack Emitter 封装。</param>
        /// <param name="controllerName">控制器名（<c>[Controller]</c> 的值）。</param>
        /// <param name="methodName">方法名。</param>
        /// <param name="args">业务参数（已剔除 CancellationToken）。</param>
        /// <param name="generated">已生成 DTO 的完整名集合，用于去重。</param>
        public static string EnsureDto(
            GeneratorExecutionContext context,
            RpcDtoMemoryPackEmitter emitter,
            string controllerName,
            string methodName,
            IList<IParameterSymbol> args,
            HashSet<string> generated)
        {
            // 0 参数不生成 DTO：调用约定是"空 Payload"，没有请求体。
            if (args.Count == 0)
            {
                return null;
            }

            // DTO 类名首字母大写：控制器名可能来自 [Controller("calc")] 这种小写字符串，
            // 直接拼会产出 calcAddRequest 这样的类名（合法但难看，也不符合 C# 命名规范）。
            var dtoName = ToPascalCase(controllerName) + methodName + "Request";
            var dtoFqn = DtoNamespace + "." + dtoName;

            // 已经生成过（同一程序集里 Controller 与 Map 可能引用同一契约）。
            if (!generated.Add(dtoFqn))
            {
                return dtoFqn;
            }

            // 先按参数名序数排序，再落成属性 —— 排序规则必须两端一致。
            var ordered = args.OrderBy(parameter => parameter.Name, StringComparer.Ordinal).ToList();

            // 收集类型用到的命名空间，供生成文件头部输出 using（这样属性类型能写短名）。
            var usingNamespaces = new HashSet<string>(StringComparer.Ordinal) { "MemoryPack" };

            var parameters = new List<RpcDtoParameter>(ordered.Count);
            for (var index = 0; index < ordered.Count; index++)
            {
                var parameter = ordered[index];
                CollectNamespaces(parameter.Type, usingNamespaces);

                parameters.Add(new RpcDtoParameter(
                    parameter.Name,
                    ToPascalCase(parameter.Name),
                    // 短名：int / string / List<int> / UserInfo，而不是 global::System.Int32 …
                    parameter.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    !parameter.Type.IsValueType,
                    index));
            }

            // 自身命名空间不需要 using。
            usingNamespaces.Remove(DtoNamespace);

            var orderedNamespaces = usingNamespaces
                .Where(name => !string.IsNullOrEmpty(name))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();

            var dtoSource = RpcDtoSourceBuilder.Emit(
                DtoNamespace,
                dtoName,
                controllerName,
                methodName,
                parameters,
                orderedNamespaces);

            var result = emitter.Emit(dtoSource, dtoFqn);
            if (!result.Success)
            {
                // 把 MemoryPack 自己给的诊断（MEMPACK0xx）也一并抛出，
                // 否则用户只知道"失败"，不知道是哪个成员类型不被支持。
                foreach (var diagnostic in result.Diagnostics)
                {
                    context.ReportDiagnostic(diagnostic);
                }

                context.ReportDiagnostic(Diagnostic.Create(
                    DtoBuildFailed,
                    Location.None,
                    controllerName,
                    methodName,
                    DescribeFailure(result)));

                // 从集合里移除，避免后续同名请求被误判为"已生成"。
                generated.Remove(dtoFqn);
                return null;
            }

            context.AddSource(dtoName + ".g.cs", SourceText.From(dtoSource, Encoding.UTF8));
            context.AddSource(dtoName + ".MemoryPack.g.cs", SourceText.From(result.FormatterSource, Encoding.UTF8));

            return dtoFqn;
        }

        /// <summary>
        /// 递归收集类型用到的命名空间（含泛型实参与数组元素类型）。
        /// </summary>
        /// <remarks>
        /// 短名解析依赖这些 <c>using</c>：漏掉一个就会让生成的 DTO 编译不过，
        /// 而且报错位置在生成文件里，排查起来比漏写 using 更绕。
        /// </remarks>
        private static void CollectNamespaces(ITypeSymbol type, HashSet<string> namespaces)
        {
            switch (type)
            {
                case IArrayTypeSymbol array:
                    CollectNamespaces(array.ElementType, namespaces);
                    return;

                case IPointerTypeSymbol pointer:
                    CollectNamespaces(pointer.PointedAtType, namespaces);
                    return;

                case INamedTypeSymbol named:
                    if (!named.ContainingNamespace.IsGlobalNamespace)
                    {
                        namespaces.Add(named.ContainingNamespace.ToDisplayString());
                    }

                    // 泛型实参也要处理：List<UserInfo> 里的 UserInfo 需要它自己的命名空间。
                    foreach (var typeArgument in named.TypeArguments)
                    {
                        CollectNamespaces(typeArgument, namespaces);
                    }

                    return;
            }
        }

        /// <summary>把失败结果翻译成人能读的一句话。</summary>
        private static string DescribeFailure(RpcDtoEmitResult result)
        {
            switch (result.Failure)
            {
                case RpcDtoEmitFailure.ValidateFailed:
                    return "MemoryPack 校验未通过（成员类型可能不可序列化）";
                case RpcDtoEmitFailure.NoGenerate:
                    return "MemoryPack 判定为 GenerateType.NoGenerate（[MemoryPackable] 可能未解析上）";
                case RpcDtoEmitFailure.MemoryPackNotReferenced:
                    return "当前编译里解析不到 MemoryPack 类型（请确认引用了 MemoryPack.Core）";
                case RpcDtoEmitFailure.SymbolNotFound:
                    return "临时 Compilation 中找不到 DTO 符号：" + result.FailureMessage;
                default:
                    return result.FailureMessage ?? "未知异常";
            }
        }

        /// <summary>
        /// 把 camelCase 参数名转成 PascalCase 属性名。
        /// </summary>
        /// <remarks>
        /// 必须<b>公开</b>并被生成器复用：DTO 的属性名（这里）和代理里的对象初始化器
        /// （<c>ControllerRpcGenerator</c>）一旦用了两套转换规则，生成代码就会编译不过
        /// 或者更糟——编译通过但字段错位。单一来源是这个方法存在的理由。
        /// </remarks>
        public static string ToPascalCase(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }
    }
}
