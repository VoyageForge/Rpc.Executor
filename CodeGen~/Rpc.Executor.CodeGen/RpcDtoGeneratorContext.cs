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
    /// <remarks>
    /// <para>移植自试验包 Rpc.CodeGen.Probe。</para>
    /// <para>
    /// <b>三处关键开关</b>（都会实际改变发射出来的代码形态）：
    /// </para>
    /// <list type="number">
    ///   <item><description><see cref="IsForUnity"/> = false：与工程内官方 MemoryPack.Generator 一致。
    ///   true 会生成非泛型 MemoryPackWriter 签名，而 NuGet 版 MemoryPack.Core 只有泛型版本。</description></item>
    ///   <item><description><see cref="IsNet7OrGreater"/> = false：Unity 没有 NET7_0_OR_GREATER，
    ///   走"嵌套 MemoryPackFormatter&lt;T&gt; + public RegisterFormatter()"分支。</description></item>
    ///   <item><description><see cref="LanguageVersion"/> = C# 9：决定 namespace 写法与 scoped 关键字。</description></item>
    /// </list>
    /// <para>
    /// <b>诊断回传</b>：<c>Validate()</c> 只把诊断塞给 context，所以这里收集到 <see cref="Diagnostics"/>，
    /// 由调用方决定如何上报。
    /// </para>
    /// </remarks>
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
            // 故意留空：若上游改成内部 AddSource，这里会静默丢代码，
            // 表现为"生成的 partial 缺内容"，比悄悄吞掉更容易发现。
        }
    }
}
