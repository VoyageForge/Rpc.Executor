namespace VoyageForge.Rpc.CodeGen
{
    /// <summary>生成源码的文本规范化。</summary>
    /// <remarks>
    /// <para>移植自试验包 Rpc.CodeGen.Probe。</para>
    /// <para>
    /// <b>为什么需要它</b>：C# 的原始字符串字面量（<c>"""..."""</c>）<b>保留源文件里的换行符</b>。
    /// MemoryPack 的 Emitter 大量使用原始字符串，于是生成代码的行尾会跟随上游源码的行尾。
    /// 统一规范成 LF 后，不同机器/不同宿主产出的生成结果才字节一致。
    /// </para>
    /// </remarks>
    public static class SourceTextNormalizer
    {
        /// <summary>把任意行尾（CRLF / CR / LF）统一成 LF。</summary>
        public static string ToUnixLineEndings(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }

            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }
    }
}
