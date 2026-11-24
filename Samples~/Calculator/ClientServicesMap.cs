using System;
using System.Threading;
using System.Threading.Tasks;
using VoyageForge.Rpc.Executor;
using VoyageForge.Rpc.Transport;

namespace VoyageForge.Rpc.Samples
{
    /// <summary>
    /// 手写的强类型 <see cref="ServicesMap"/> 子类：把"service + method 字符串 + 参数打包"
    /// 收敛成一个个方法，业务层只看到强类型签名。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>这是旧协议的写法</b>（<c>[RpcService]</c> + <c>ServicesMap</c>）。新代码建议用
    /// Controller 模式（<c>CalculatorControllerMap</c>）：那边 service/method/payload/DTO
    /// 全部由生成器产出，这里要靠手写字符串，拼错了只有运行时才报错。
    /// </para>
    /// <para>
    /// 保留它是因为它演示了一个仍然有用的场景：<b>调用端不想/不能引入生成代理时</b>
    /// （比如服务名是运行时配置、或者调用端在纯 .NET 环境里只引用了 Runtime 程序集）。
    /// </para>
    /// </remarks>
    public sealed class ClientServicesMap : ServicesMap
    {
        /// <summary>
        /// 服务端注册名。
        /// 必须与 <c>[RpcService("user")]</c> 里的字符串一致 —— 提成常量是为了让改名字时只有一个地方要改。
        /// </summary>
        public const string UserServiceName = "user";

        /// <summary>方法名常量。与服务端 <c>[RpcMethod]</c> 的方法名一致。</summary>
        private const string GetNameMethod = nameof(GetName);

        /// <summary>
        /// 预取的 <c>user</c> 服务命名空间。
        /// </summary>
        /// <remarks>
        /// 每个 <see cref="ServicesMap.Service"/> 调用都会新建一个 <see cref="ServiceApi"/>，
        /// 在字段里缓存一份可以避免每次请求都重复分配。
        /// <b>前提是服务名不可变</b> —— 本类把它固定成常量，所以安全。
        /// </remarks>
        private readonly ServiceApi _user;

        /// <summary>用本地执行器构造（单机 / 测试）。</summary>
        public ClientServicesMap(RpcExecutor executor) : base(executor)
        {
            _user = Service(UserServiceName);
        }

        /// <summary>用自定义发送委托构造（例如接入 TcpSession）。</summary>
        public ClientServicesMap(Func<RpcRequest, CancellationToken, Task<RpcResponse>> send) : base(send)
        {
            _user = Service(UserServiceName);
        }

        /// <summary>
        /// 调用 <c>user.GetName</c>。
        /// </summary>
        /// <param name="userId">用户 Id，不能为负。</param>
        /// <param name="cancellationToken">取消令牌，透传给底层传输。</param>
        /// <returns>强类型响应；<c>Success</c> 为 false 时读 <c>Error</c>。</returns>
        /// <remarks>
        /// 这里<b>不写 <c>async/await</c></b>：直接返回 Task 少一次状态机分配，
        /// 异常和取消都由 Task 本身传递，不需要额外处理。
        /// </remarks>
        public Task<RpcResponse<string>> GetName(int userId, CancellationToken cancellationToken = default(CancellationToken))
        {
            // 提前校验而不是让服务端去判断：参数错误是调用方的 bug，
            // 在本地就抛出来比走一趟网络再拿错误码更容易定位。
            if (userId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(userId), userId, "userId 不能为负数。");
            }

            return _user.CallAsync<string>(GetNameMethod, RpcPayload.Pack(userId), cancellationToken);
        }
    }
}
