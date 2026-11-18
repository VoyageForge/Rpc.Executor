namespace VoyageForge.Rpc.Samples.Handlers
{
    /// <summary>同步强类型请求：用 IRequestHandler 通道处理。</summary>
    public sealed class EchoRequest
    {
        /// <summary>请求文本。</summary>
        public string Text { get; set; }
    }
}