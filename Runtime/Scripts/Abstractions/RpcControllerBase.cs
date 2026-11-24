using System;
using VoyageForge.Rpc.Transport.Session;

namespace VoyageForge.Rpc.Executor
{
    /// <summary>本地 RPC Controller 基类。每次调用由生成的 Dispatcher 关联当前连接的 Session。</summary>
    public abstract class RpcControllerBase
    {
        /// <summary>当前请求所属的 Session，可访问该 Session 独有的 Blackboard。</summary>
        protected Session Session { get; private set; }

        /// <summary>由生成的 Catalog 在调用前注入；业务代码不应主动调用。</summary>
        public void SetSession(Session session)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
        }
    }
}
