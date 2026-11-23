using VoyageForge.Rpc.Transport;

namespace VoyageForge.Rpc.Executor
{
    /// <summary>默认上下文工厂：从请求头 user-id / permissions（逗号分隔）解析。</summary>
    public sealed class DefaultRpcContextFactory : IRpcContextFactory
    {
        /// <summary>由请求头构建上下文。</summary>
        public RpcContext Create(RpcRequest request)
        {
            return RpcContext.FromHeaders(request?.Headers);
        }
    }
}