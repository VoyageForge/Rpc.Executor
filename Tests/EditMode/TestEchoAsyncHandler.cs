using System.Threading;
using Cysharp.Threading.Tasks;
using VoyageForge.Rpc.Runtime.Handlers;

namespace VoyageForge.Rpc.Executor.Tests
{
    /// <summary>异步处理器：验证强类型 SendAsync 路径与取消令牌透传。</summary>
    public sealed class TestEchoAsyncHandler : IAsyncRequestHandler<TestAsyncEchoRequest, TestAsyncEchoResponse>
    {
        /// <summary>记录最后一次收到的取消令牌，便于断言透传行为。</summary>
        public CancellationToken LastCancellationToken { get; private set; }

        /// <inheritdoc />
        public UniTask<TestAsyncEchoResponse> HandleAsync(TestAsyncEchoRequest request,
            CancellationToken cancellationToken)
        {
            LastCancellationToken = cancellationToken;
            return UniTask.FromResult(new TestAsyncEchoResponse
            {
                Text = "async:" + (request == null ? "<null>" : request.Text)
            });
        }
    }
}