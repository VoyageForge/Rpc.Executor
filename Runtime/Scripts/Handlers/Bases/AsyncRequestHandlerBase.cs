using System.Threading;
using Cysharp.Threading.Tasks;

namespace VoyageForge.Rpc.Runtime.Handlers
{
    /// <summary>
    /// 异步处理器基类。继承本类后无法再继承 <see cref="RequestHandlerBase{TRequest,TResponse}"/>，
    /// 从而在类型层面避免“同步/异步同时实现”。
    /// </summary>
    /// <typeparam name="TRequest">请求类型。</typeparam>
    /// <typeparam name="TResponse">响应类型。</typeparam>
    public abstract class AsyncRequestHandlerBase<TRequest, TResponse>
        : IAsyncRequestHandler<TRequest, TResponse>
    {
        /// <inheritdoc />
        public abstract UniTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
    }
}