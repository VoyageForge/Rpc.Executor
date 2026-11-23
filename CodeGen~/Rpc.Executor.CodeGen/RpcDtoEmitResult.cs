using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace VoyageForge.Rpc.CodeGen
{
    /// <summary>一次"复用 MemoryPack Emitter"尝试的结果。</summary>
    /// <remarks>移植自试验包 Rpc.CodeGen.Probe。</remarks>
    public sealed class RpcDtoEmitResult
    {
        private RpcDtoEmitResult(
            bool success,
            string? formatterSource,
            RpcDtoEmitFailure failure,
            string? failureMessage,
            IReadOnlyList<Diagnostic> diagnostics)
        {
            Success = success;
            FormatterSource = formatterSource;
            Failure = failure;
            FailureMessage = failureMessage;
            Diagnostics = diagnostics;
        }

        /// <summary>是否成功拿到完整的序列化源码。</summary>
        public bool Success { get; }

        /// <summary>成功时的完整 C# 文件源码；失败时为 null。</summary>
        public string? FormatterSource { get; }

        /// <summary>失败原因分类。</summary>
        public RpcDtoEmitFailure Failure { get; }

        /// <summary>失败详情（异常消息等），用于拼诊断文本。</summary>
        public string? FailureMessage { get; }

        /// <summary>MemoryPack 自己在 Validate 阶段报告的诊断（MEMPACK0xx）。</summary>
        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        /// <summary>构造一个失败结果。</summary>
        public static RpcDtoEmitResult Fail(
            RpcDtoEmitFailure failure,
            string? message,
            IReadOnlyList<Diagnostic>? diagnostics = null)
        {
            return new RpcDtoEmitResult(false, null, failure, message, diagnostics ?? Array.Empty<Diagnostic>());
        }

        /// <summary>构造一个成功结果。</summary>
        public static RpcDtoEmitResult Ok(string formatterSource, IReadOnlyList<Diagnostic> diagnostics)
        {
            return new RpcDtoEmitResult(true, formatterSource, RpcDtoEmitFailure.None, null, diagnostics);
        }
    }
}
