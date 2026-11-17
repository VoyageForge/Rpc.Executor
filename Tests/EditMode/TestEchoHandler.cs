using System;
using VoyageForge.Rpc.Runtime.Handlers;

namespace VoyageForge.Rpc.Executor.Tests
{
    /// <summary>同步处理器：把请求文本加前缀后返回，用于验证强类型 Send 路径。</summary>
    public sealed class TestEchoHandler : IRequestHandler<TestEchoRequest, TestEchoResponse>
    {
        /// <summary>记录最后一次收到的请求，便于断言对象是否原样传入。</summary>
        public TestEchoRequest LastRequest { get; private set; }

        /// <inheritdoc />
        public TestEchoResponse Handle(TestEchoRequest request)
        {
            LastRequest = request;
            return new TestEchoResponse { Text = "sync:" + (request == null ? "<null>" : request.Text) };
        }
    }
}
