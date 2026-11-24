using System.Threading;
using System.Threading.Tasks;
using VoyageForge.Rpc.Transport.Session;

namespace VoyageForge.Rpc.Samples.Contracts
{
    /// <summary>调用方持有的协议声明；被调用方不需要引用此接口。</summary>
    [RpcContract("Session")]
    public interface ISessionApi
    {
        Task<string> ConnectAsync(string userId, string token, CancellationToken ct);
    }
}
