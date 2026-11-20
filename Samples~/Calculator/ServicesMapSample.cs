using System;
using System.Threading;
using System.Threading.Tasks;
using PlasticPipe.PlasticProtocol.Messages;
using VoyageForge.Rpc.Executor;
using VoyageForge.Rpc.Executor.Abstractions;
using VoyageForge.Rpc.Transport;

namespace VoyageForge.Rpc.Samples
{
    public class ClientServicesMap : ServicesMap
    {
        public ClientServicesMap(RpcExecutor executor) : base(executor)
        {
        }

        public ClientServicesMap(Func<RpcRequest, CancellationToken, Task<RpcResponse>> send) : base(send)
        {
        }

        public async Task<RpcResponse<string>> GetName(int userId)
        {
            return await CallAsync<string>("user", "GetName", userId);
        }
    }

    /// <summary>
    /// ServicesMap（调用端，axios 风格）示例：
    /// 业务只写 service + method 字符串，ServicesMap 负责序列化、发送、拿回强类型响应。
    /// </summary>
    public static class ServicesMapSample
    {
        [RpcService("user")]
        public interface IUserService
        {
            /// <summary>按 Id 查用户名。</summary>
            string GetName(int userId);
        }

        public sealed class UserService : IUserService
        {
            public string GetName(int userId)
            {
                return "user-" + userId;
            }
        }


        /// <summary>构建服务端执行器（示例里本地直接执行；真实项目换成 TcpSession 发送）。</summary>
        public static RpcExecutor CreateExecutor()
        {
            ServiceRegistry registry = new ServiceRegistry();
            registry.Register<ICalculator>(new Calculator(), "calc");
            registry.Register<IUserService>(new UserService(), "user");

            IGeneratedRpcDispatcher dispatcher = new VoyageForge.Rpc.Generated.RpcGeneratedDispatcher();
            return new RpcExecutor(
                registry,
                new GeneratedMethodResolver(dispatcher),
                new GeneratedRpcInvoker(dispatcher))
            {
                MethodNameResolver = VoyageForge.Rpc.Generated.RpcMethodNames.TryGetName
            };
        }

        /// <summary>调用端：像 axios 一样按 service + method 调用。</summary>
        public static async Task RunAsync()
        {
            var api = new ClientServicesMap(CreateExecutor());

            // axios 风格：api.CallAsync<T>(service, method, params...)
            RpcResponse<int> add = await api.CallAsync<int>("calc", "Add", 3, 4);
            UnityEngine.Debug.Log("calc.Add(3,4) = " + add.Data);

            // 也可以先进入服务命名空间：api.Service("user").CallAsync<T>(method, params...)
            RpcResponse<string> name = await api.GetName(7);
            UnityEngine.Debug.Log("user.GetName(7) = " + name.Data);
        }
    }
}