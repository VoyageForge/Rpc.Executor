using VoyageForge.Rpc.Transport;

namespace VoyageForge.Rpc.Executor
{
    /// <summary>
    /// 调用上下文工厂：把 RpcRequest（含 Headers）解析成 RpcContext。
    /// 默认实现从 Headers 读取 user-id / permissions；可替换为 JWT、数据库、缓存等来源。
    /// </summary>
    public interface IRpcContextFactory
    {
        /// <summary>由请求构建上下文。</summary>
        RpcContext Create(RpcRequest request);
    }
}