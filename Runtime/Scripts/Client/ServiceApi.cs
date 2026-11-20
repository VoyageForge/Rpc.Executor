using System.Threading;
using System.Threading.Tasks;
using VoyageForge.Rpc.Transport;

namespace VoyageForge.Rpc.Executor
{
    /// <summary>单个服务的 API 命名空间（axios 的 api.calc.Add(...) 形态）。</summary>
    public sealed class ServiceApi
    {
        private readonly ServicesMap _map;
        private readonly string _serviceName;

        /// <summary>创建服务 API 命名空间。</summary>
        internal ServiceApi(ServicesMap map, string serviceName)
        {
            _map = map;
            _serviceName = serviceName;
        }

        /// <summary>调用本服务的方法，Payload 由调用方自行序列化。</summary>
        public Task<RpcResponse<TResponse>> CallAsync<TResponse>(string methodName, byte[] payload,
            CancellationToken ct = default(CancellationToken))
        {
            return _map.CallAsync<TResponse>(_serviceName, methodName, payload, ct);
        }
    }
}