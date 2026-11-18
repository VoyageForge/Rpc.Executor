using VoyageForge.Rpc.Runtime.Handlers;

namespace VoyageForge.Rpc.Samples.Handlers
{
    /// <summary>同步处理器示例：一个请求类型对应一个同步处理器。</summary>
    public sealed class EchoHandler : IRequestHandler<EchoRequest, EchoResponse>
    {
        /// <summary>同步处理请求。</summary>
        public EchoResponse Handle(EchoRequest request)
        {
            return new EchoResponse { Text = "sync:" + (request == null ? "<null>" : request.Text) };
        }
    }
}