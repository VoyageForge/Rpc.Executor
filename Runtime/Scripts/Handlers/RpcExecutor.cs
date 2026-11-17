using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace VoyageForge.Rpc.Runtime.Handlers
{
    /// <summary>
    /// Rpc 执行器默认实现（实例类，不是静态类）：
    /// <list type="bullet">
    /// <item>一个请求类型只允许一个处理器（同步或异步二选一）；</item>
    /// <item><see cref="Send{TRequest,TResponse}"/> 只走同步处理器；</item>
    /// <item><see cref="SendAsync{TRequest,TResponse}"/> 只走异步处理器；</item>
    /// <item>找不到或类型不匹配时抛出明确的异常，不做隐式降级。</item>
    /// </list>
    /// <see cref="RpcExecutor"/> 是普通实例类：调用方自行创建或注入，
    /// 需要单例时由使用者在自己的服务（如 sample 的 GameRpc）里包一层。
    /// </summary>
    public sealed class RpcExecutor : IRpcExecutor
    {
        /// <inheritdoc />
        public TResponse Send<TRequest, TResponse>(TRequest request)
        {
            if (!RpcHandlerCatalogCache.TryGet(typeof(TRequest), out object handler, out bool isAsync))
            {
                throw new InvalidOperationException($"没有为 {typeof(TRequest)} 注册处理器。");
            }

            if (isAsync)
            {
                throw new InvalidOperationException($"{typeof(TRequest)} 只有异步处理器，请使用 SendAsync。");
            }

            if (handler is IRequestHandler<TRequest, TResponse> sync)
            {
                return sync.Handle(request);
            }

            throw new InvalidOperationException(
                $"处理器 {handler.GetType()} 与 {typeof(TRequest)} / {typeof(TResponse)} 不匹配。");
        }

        /// <inheritdoc />
        public UniTask<TResponse> SendAsync<TRequest, TResponse>(
            TRequest request,
            CancellationToken cancellationToken = default)
        {
            if (!RpcHandlerCatalogCache.TryGet(typeof(TRequest), out object handler, out bool isAsync))
            {
                throw new InvalidOperationException($"没有为 {typeof(TRequest)} 注册处理器。");
            }

            if (!isAsync)
            {
                throw new InvalidOperationException($"{typeof(TRequest)} 只有同步处理器，请使用 Send。");
            }

            if (handler is IAsyncRequestHandler<TRequest, TResponse> async)
            {
                return async.HandleAsync(request, cancellationToken);
            }

            throw new InvalidOperationException(
                $"处理器 {handler.GetType()} 与 {typeof(TRequest)} / {typeof(TResponse)} 不匹配。");
        }
    }
}