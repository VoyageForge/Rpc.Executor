namespace VoyageForge.Rpc.Executor.Tests
{
    /// <summary>测试用异步请求：与同步请求分开，保证同一请求类型只绑定一种处理器。</summary>
    public sealed class TestAsyncEchoRequest
    {
        /// <summary>请求文本。</summary>
        public string Text { get; set; }
    }
}