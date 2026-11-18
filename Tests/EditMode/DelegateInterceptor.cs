using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoyageForge.Rpc.Executor.Abstractions;
using VoyageForge.Rpc.Transport;

namespace VoyageForge.Rpc.Executor.Tests
{
    /// <summary>测试拦截器：记录调用顺序并继续执行内层。</summary>
    public sealed class DelegateInterceptor : IRpcInterceptor
    {
        private readonly string _name;
        private readonly List<string> _calls;

        /// <summary>创建命名拦截器。</summary>
        public DelegateInterceptor(string name, List<string> calls)
        {
            _name = name;
            _calls = calls;
        }

        /// <inheritdoc />
        public async Task<RpcResponse> InvokeAsync(RpcRequest request, Func<Task<RpcResponse>> next,
            CancellationToken ct)
        {
            _calls.Add(_name + ":before");
            RpcResponse response = await next();
            _calls.Add(_name + ":after");
            return response;
        }
    }
}