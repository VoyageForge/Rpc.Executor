using System.Threading;
using Cysharp.Threading.Tasks;

namespace VoyageForge.Rpc.Runtime.Handlers
{
    /// <summary>异步请求处理器：一个请求类型对应一个处理器（与同步处理器互斥）。</summary>
    /// <typeparam name="TRequest">请求类型。</typeparam>
    /// <typeparam name="TResponse">响应类型。</typeparam>
    public interface IAsyncRequestHandler<TRequest, TResponse>
    {
        /// <summary>异步处理请求并返回响应。</summary>
        /// <param name="request">请求对象。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>响应。</returns>
        UniTask<TResponse> HandleAsync(TRequest request, CancellationToken cancellationToken);
    }
}