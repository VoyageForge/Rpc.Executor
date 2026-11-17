namespace VoyageForge.Rpc.Runtime.Handlers
{
    /// <summary>
    /// 同步处理器基类。C# 单继承：继承本类后无法再继承 <see cref="AsyncRequestHandlerBase{TRequest,TResponse}"/>，
    /// 从而在类型层面避免“同步/异步同时实现”。
    /// </summary>
    /// <typeparam name="TRequest">请求类型。</typeparam>
    /// <typeparam name="TResponse">响应类型。</typeparam>
    public abstract class RequestHandlerBase<TRequest, TResponse>
        : IRequestHandler<TRequest, TResponse>
    {
        /// <inheritdoc />
        public abstract TResponse Handle(TRequest request);
    }
}