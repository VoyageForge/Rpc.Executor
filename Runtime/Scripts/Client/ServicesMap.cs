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
            _send = executor.ExecuteAsync;
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

        /// <summary>
        /// axios 风格调用：service + method + 已序列化 Payload。
        /// 服务端把整个 RpcResponse&lt;T&gt; 序列化进 Payload（返回值必须由 RpcResponse 包裹）。
        /// </summary>
        public async Task<RpcResponse<TResponse>> CallAsync<TResponse>(string serviceName, string methodName,
            byte[] payload, CancellationToken ct = default(CancellationToken))
        {
            RpcResponse baseResponse = await _send(new RpcRequest
            {
                ServiceName = serviceName,
                MethodName = methodName,
                Payload = payload ?? Array.Empty<byte>()
            }, ct).ConfigureAwait(false);

            if (!baseResponse.Success)
            {
                return new RpcResponse<TResponse>
                {
                    Success = false,
                    Error = baseResponse.Error,
                    RequestId = baseResponse.RequestId,
                    MethodId = baseResponse.MethodId,
                    Detail = baseResponse.Detail
                };
            }

            // 服务端契约返回 RpcResponse<T>，dispatch 把 Data 序列化进 Payload。
            if (baseResponse.Payload != null && baseResponse.Payload.Length > 0)
            {
                return new RpcResponse<TResponse>
                {
                    Success = true,
                    Data = RpcPayload.Unpack<TResponse>(baseResponse.Payload),
                    RequestId = baseResponse.RequestId,
                    MethodId = baseResponse.MethodId
                };
            }

            return new RpcResponse<TResponse>
            {
                Success = true,
                RequestId = baseResponse.RequestId,
                MethodId = baseResponse.MethodId
            };
        }

    }
}