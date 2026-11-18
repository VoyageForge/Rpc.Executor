namespace VoyageForge.Rpc.Executor.Tests
{
    /// <summary>测试用强类型请求：承载一段文本，便于断言处理器是否收到正确对象。</summary>
    public sealed class TestEchoRequest
    {
        /// <summary>请求文本。</summary>
        public string Text { get; set; }
    }
}