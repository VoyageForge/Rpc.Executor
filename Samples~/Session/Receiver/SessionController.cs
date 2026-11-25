using System;
using System.Threading;
using System.Threading.Tasks;
using VoyageForge.Rpc.Transport;

namespace VoyageForge.Rpc.Samples.Receiver
{
    /// <summary>被调用方只声明本地 Controller，不引用调用方的 ISessionApi。</summary>
    [RpcController("Session")]
    public partial class SessionController : RpcControllerBase
    {
        /// <summary>方法名、参数类型及顺序与远端契约对应；参数名称可以不同。</summary>
        [RpcMethod]
        public Task<string> WhoAmIAsync()
        {
            if (Session.Blackboard.TryGetValue("User", out object user) && user is string account)
                return Task.FromResult(account);
            throw new InvalidOperationException("请先登录。");
        }

        [RpcMethod]
        public Task<string> LoginAsync(string account, string credential, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            // 鉴权完全由业务实现决定。示例只展示从当前 Session 的 Blackboard 读取应用回调。
            if (!Session.Blackboard.TryGetValue("Authenticate", out object callback) ||
                !(callback is Func<string, string, bool> authenticate) || !authenticate(account, credential))
                throw new InvalidOperationException("身份验证失败。");

            Session.Blackboard["User"] = account;
            return Task.FromResult("Connected: " + account);
        }
    }
}
