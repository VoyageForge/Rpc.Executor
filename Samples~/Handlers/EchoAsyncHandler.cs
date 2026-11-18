using System.Threading;
using Cysharp.Threading.Tasks;
using VoyageForge.Rpc.Runtime.Handlers;

namespace VoyageForge.Rpc.Samples.Handlers
{
    /// <summary>异步处理器示例：与同步处理器互斥，一个请求类型只能有一种处理器。</summary>
    public sealed class EchoAsyncHandler : IAsyncRequestHandler<EchoAsyncRequest, EchoResponse>
    {
        /// <summary>异步处理请求。</summary>
        public UniTask<EchoResponse> HandleAsync(EchoAsyncRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return UniTask.FromResult(new EchoResponse
            {
                Text = "async:" + (request == null ? "<null>" : request.Text)
            });
        }
    }
}