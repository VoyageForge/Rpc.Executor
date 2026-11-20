using System;
using System.Threading;
using System.Threading.Tasks;
using VoyageForge.Rpc.Executor.Abstractions;
using VoyageForge.Rpc.Transport;

namespace VoyageForge.Rpc.Executor
{
    /// <summary>
    /// 调用端 API 封装（axios 风格）：持有执行器/传输，业务只需要 service + method 字符串。
    /// </summary>
    public  class ServicesMap
    {
        private readonly Func<RpcRequest, CancellationToken, Task<RpcResponse>> _send;

        /// <summary>用本地 RpcExecutor 构造（单机/测试）。</summary>
        public ServicesMap(RpcExecutor executor)
        {
            if (executor == null) throw new ArgumentNullException(nameof(executor));
            _send = (request, ct) => executor.ExecuteAsync(request, ct);
        }

        /// <summary>用自定义发送委托构造（例如接入 TcpSession）。</summary>
        public ServicesMap(Func<RpcRequest, CancellationToken, Task<RpcResponse>> send)
        {
            _send = send ?? throw new ArgumentNullException(nameof(send));
        }

        /// <summary>进入某个服务的 API 命名空间。</summary>
        public ServiceApi Service(string serviceName)
        {
            return new ServiceApi(this, serviceName);
        }

        /// <summary>axios 风格调用：service + method + 已序列化 Payload。</summary>
        public async Task<RpcResponse<TResponse>> CallAsync<TResponse>(string serviceName, string methodName,
            byte[] payload, CancellationToken ct = default(CancellationToken))
        {
            RpcResponse baseResponse = await _send(new RpcRequest
            {
                ServiceName = serviceName,
                MethodName = methodName,
                Payload = payload ?? Array.Empty<byte>()
            }, ct).ConfigureAwait(false);

            RpcResponse<TResponse> typed = new RpcResponse<TResponse>
            {
                Success = baseResponse.Success,
                Error = baseResponse.Error,
                RequestId = baseResponse.RequestId,
                MethodId = baseResponse.MethodId,
                Detail = baseResponse.Detail
            };

            if (baseResponse.Success && baseResponse.Result != null)
            {
                typed.Data = baseResponse.Result is TResponse value
                    ? value
                    : (TResponse)Convert.ChangeType(baseResponse.Result, typeof(TResponse));
            }

            return typed;
        }

        /// <summary>axios 风格调用：service + method + params 参数。</summary>
        public Task<RpcResponse<TResponse>> CallAsync<TResponse>(string serviceName, string methodName,
            params object[] arguments)
        {
            return CallAsync<TResponse>(serviceName, methodName, RpcPayload.Pack(arguments));
        }
    }
}