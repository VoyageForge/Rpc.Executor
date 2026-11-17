using System;
using System.Threading;
using System.Threading.Tasks;
using Rpc.Executor.Abstractions;
using VoyageForge.Rpc.Transport;

namespace VoyageForge.Rpc.Executor.Tests
{
    /// <summary>测试拦截器：直接返回错误，验证短路行为。</summary>
    public sealed class ShortCircuitInterceptor : IRpcInterceptor
    {
        /// <inheritdoc />
        public Task<RpcResponse> InvokeAsync(RpcRequest request, Func<Task<RpcResponse>> next,
            CancellationToken ct)
        {
            return Task.FromResult(RpcResponse.Fail("BLOCKED", "拦截器拒绝请求。"));
        }
    }
}