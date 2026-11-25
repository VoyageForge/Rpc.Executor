using System.Collections.Generic;
using System.Threading;
using MemoryPack.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace VoyageForge.Rpc.CodeGen
{
    /// <summary>
    /// <see cref="IGeneratorContext"/> 的实现：把 MemoryPack Emitter 需要的运行环境信息
    /// 适配到本生成器上。
    /// </summary>
    public sealed class RpcDtoGeneratorContext : IGeneratorContext
    {
        private readonly List<Diagnostic> diagnostics = new List<Diagnostic>();

        /// <summary>构造一个生成器上下文。</summary>
        /// <param name="cancellationToken">生成过程被取消时用于中断。</param>
        /// <param name="languageVersion">目标语言版本（Unity 2022.3 为 C# 9）。</param>
        public RpcDtoGeneratorContext(CancellationToken cancellationToken, LanguageVersion languageVersion)
        {
            CancellationToken = cancellationToken;
            LanguageVersion = languageVersion;
        }

        /// <inheritdoc />
        public CancellationToken CancellationToken { get; }

        /// <inheritdoc />
        public LanguageVersion LanguageVersion { get; }

        /// <summary>目标运行时是否是 .NET 7+。Unity 固定 false。</summary>
        public bool IsNet7OrGreater => false;

        /// <summary>是否走 Unity 专用分支。固定 false，与工程内官方 analyzer 行为一致。</summary>
        public bool IsForUnity => false;

        /// <summary>收集到的诊断（调用方负责上报）。</summary>
        public IReadOnlyList<Diagnostic> Diagnostics => diagnostics;

        /// <inheritdoc />
        public void ReportDiagnostic(Diagnostic diagnostic)
        {
            diagnostics.Add(diagnostic);
        }

        /// <summary>
        /// 接口要求实现，但实际不会被调用：MemoryPack 的 <c>TypeMeta.Emit()</c>
        /// 只往传入的 StringBuilder 里写代码，不发源。
        /// </summary>
        public void AddSource(string hintName, string source)
        {
            // TypeMeta.Emit 将 formatter 写入调用方传入的 StringBuilder。
        }
    }
}
