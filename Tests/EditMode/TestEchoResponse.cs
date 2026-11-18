namespace VoyageForge.Rpc.Executor.Tests
{
    /// <summary>测试用强类型响应：返回处理器处理后的文本。</summary>
    public sealed class TestEchoResponse
    {
        /// <summary>响应文本。</summary>
        public string Text { get; set; }
    }
}