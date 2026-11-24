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
    /// 调用方 Session 与被调用方 Controller 程序集是<b>分开编译</b>的，
    /// 两边都需要同一个 DTO。这个工厂用<b>确定性的命名规则</b>保证两端算出同一个类型全名：
    /// </para>
    /// <code>
    /// VoyageForge.Rpc.Generated.Dto.{ControllerPart}{MethodPart}_{SignatureHash}Request
    /// </code>
    /// <para>
    /// 同一程序集内如果 Session 代理和本地 Controller 使用同一协议方法，会重复请求同一个 DTO ——
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
        private static readonly SymbolDisplayFormat ProtocolFqn = SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions & ~SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);
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

            // DTO 类型名仅供生成器内部使用：协议名清洗成合法标识符，完整协议签名哈希用于避免重名和重载冲突。
            string signature = string.Join("\u001f", args.Select(parameter => parameter.Type.ToDisplayString(ProtocolFqn)));
            ulong signatureHash = StableHash("v2\u001f" + controllerName + "\u001f" + methodName + "\u001f" + signature);
            var dtoName = ToIdentifierPart(controllerName) + ToIdentifierPart(methodName) + "_" + signatureHash.ToString("X16") + "Request";
            var dtoFqn = DtoNamespace + "." + dtoName;

            // 已经生成过（同一程序集里 Session 代理和本地 Controller 可能对应同一协议方法）。
            if (!generated.Add(dtoFqn))
            {
                return dtoFqn;
            }

            // DTO 成员严格遵循方法参数声明顺序。参数名只影响生成属性名，不参与协议布局。
            // 两端即使参数名不同，只要参数类型与顺序一致，MemoryPack 二进制布局仍一致。
            var ordered = args.ToList();

            var parameters = new List<RpcDtoParameter>(ordered.Count);
            for (var index = 0; index < ordered.Count; index++)
            {
                var parameter = ordered[index];
                parameters.Add(new RpcDtoParameter(
                    parameter.Name,
                    "Arg" + index,
                    // 使用完整类型名，避免两端或同一方法的不同参数存在同名类型时发生 using 冲突。
                    parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    !parameter.Type.IsValueType,
                    index));
            }

            var dtoSource = RpcDtoSourceBuilder.Emit(
                DtoNamespace,
                dtoName,
                controllerName,
                methodName,
                parameters);

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

        private static ulong StableHash(string value)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            foreach (byte valueByte in Encoding.UTF8.GetBytes(value ?? string.Empty))
            {
                hash ^= valueByte;
                hash = unchecked(hash * prime);
            }
            return hash;
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
        /// 把协议名称转换为合法的 DTO 标识符片段。
        /// </summary>
        /// <remarks>用于 DTO 类型名；清洗协议名称中的非标识符字符。</remarks>
        private static string ToIdentifierPart(string value)
        {
            string safe = new string((value ?? string.Empty)
                .Select(character => char.IsLetterOrDigit(character) || character == '_' ? character : '_')
                .ToArray());
            if (safe.Length == 0) safe = "_";
            if (char.IsDigit(safe[0])) safe = "_" + safe;
            return ToPascalCase(safe);
        }

        private static string ToPascalCase(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return name;
            }

            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }
    }
}
