using System.Threading;
using Cysharp.Threading.Tasks;

namespace VoyageForge.Rpc.Runtime.Handlers
{
    /// <summary>
    /// Rpc 执行器契约：把强类型请求交给注册的处理器。
    /// 用实例而非静态类，便于替换/注入/测试。
    /// </summary>
    public interface IRpcExecutor
    {
        /// <summary>同步执行。</summary>
        TResponse Send<TRequest, TResponse>(TRequest request);

        /// <summary>异步执行。</summary>
        UniTask<TResponse> SendAsync<TRequest, TResponse>(TRequest request, CancellationToken cancellationToken = default);
    }
}