using System.Threading.Tasks;
using VoyageForge.Rpc.Executor;
using VoyageForge.Rpc.Transport;
using UnityEngine;

namespace VoyageForge.Rpc.Samples
{
    /// <summary>
    /// 旧协议（<c>[RpcService]</c> + <c>ServicesMap</c>）示例：业务只写 service + method 字符串，
    /// ServicesMap 负责序列化、发送、拿回强类型响应。
    /// </summary>
    /// <remarks>
    /// <b>与 Controller 模式的区别</b>：这里服务端是"标了 <c>[RpcService]</c> 的 class"，
    /// 方法用 <c>[RpcMethod]</c> 标注、返回 <c>RpcResponse&lt;T&gt;</c>；
    /// 而 Controller 模式用 <c>[Controller]</c> 接口 + <c>[RpcController]</c> 实现，
    /// 方法返回 <c>Task&lt;T&gt;</c>、请求 DTO 自动生成、调用端走 <c>ControllerMap</c> 代理。
    /// 两种模式目前并存，新代码建议用 Controller。
    /// </remarks>
    public static class ServicesMapSample
    {
        /// <summary>旧协议的服务端形态：partial + [RpcService] + [RpcMethod]。</summary>
        [RpcService("user")]
        public sealed partial class UserController : RpcControllerBase
        {
            /// <summary>按 Id 查用户名。</summary>
            [RpcMethod]
            public RpcResponse<string> GetName(int userId) => Ok("user-" + userId);
        }

        /// <summary>构建服务端执行器（示例里本地直接执行；真实项目换成 TcpSession 发送）。</summary>
        public static RpcExecutor CreateExecutor()
        {
            ServiceRegistry registry = new ServiceRegistry();
            registry.Register<UserController>(new UserController(), "user");

            IGeneratedRpcDispatcher dispatcher = new VoyageForge.Rpc.Generated.RpcGeneratedDispatcher();
            return new RpcExecutor(
                registry,
                new GeneratedMethodResolver(dispatcher),
                new GeneratedRpcInvoker(dispatcher))
            {
                MethodNameResolver = VoyageForge.Rpc.Generated.RpcMethodNames.TryGetName
            };
        }

        /// <summary>调用端：按 service + method 字符串发起调用。</summary>
        public static async Task RunAsync()
        {
            ClientServicesMap api = new ClientServicesMap(CreateExecutor());

            RpcResponse<string> name = await api.GetName(7);
            Debug.Log("user.GetName(7) = " + name.Data);
        }
    }
}
