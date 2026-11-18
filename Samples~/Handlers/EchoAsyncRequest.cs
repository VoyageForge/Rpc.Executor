namespace VoyageForge.Rpc.Samples.Handlers
{
    /// <summary>异步强类型请求：用 IAsyncRequestHandler 通道处理。</summary>
    public sealed class EchoAsyncRequest
    {
        /// <summary>请求文本。</summary>
        public string Text { get; set; }
    }
}