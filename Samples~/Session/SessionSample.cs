
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using VoyageForge.Rpc.Samples.Caller;
using VoyageForge.Rpc.Samples.Contracts;
using VoyageForge.Rpc.Samples.Receiver;
using VoyageForge.Rpc.Transport;

namespace VoyageForge.Rpc.Samples
{

    /// <summary>
    /// 最小装配示例。真实项目通常把 Caller 与 Receiver 放在不同程序集：
    /// 调用方程序集只包含契约与 Session；被调用方程序集只包含 Controller。
    /// </summary>
    public static class SessionSample
    {
        public static GameSession CreateSession(TcpClient connectedClient)
        {
            var session = GameSession.Create();
            session.Blackboard["Authenticate"] = new Func<string, string, bool>(
                (userId, token) => !string.IsNullOrWhiteSpace(userId) && !string.IsNullOrWhiteSpace(token));
            session.Attach(connectedClient);
            return session;
        }

        public static Task<string> LoginAsync(GameSession session, string userId, string token, CancellationToken ct)
        {
            return session.Remote<ISessionApi>().LoginAsync(userId, token, ct);
        }
    }
}
