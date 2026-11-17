namespace VoyageForge.Rpc.Runtime.Handlers
{
    /// <summary>同步请求处理器：一个请求类型对应一个处理器。</summary>
    /// <typeparam name="TRequest">请求类型。</typeparam>
    /// <typeparam name="TResponse">响应类型。</typeparam>
    public interface IRequestHandler<TRequest, TResponse>
    {
        /// <summary>处理请求并返回响应。</summary>
        /// <param name="request">请求对象。</param>
        /// <returns>响应。</returns>
        TResponse Handle(TRequest request);
    }
}