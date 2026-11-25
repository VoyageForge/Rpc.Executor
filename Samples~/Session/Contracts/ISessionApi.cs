using System.Threading;
using System.Threading.Tasks;
using VoyageForge.Rpc.Transport;

namespace VoyageForge.Rpc.Samples.Contracts
{
    /// <summary>
    /// 调用方持有的协议声明；被调用方不需要引用此接口。
    /// </summary>
    [RpcContract("Session")]
    public partial interface ISessionApi
    {
        Task<string> WhoAmIAsync();
        Task<string> LoginAsync(string userId, string token, CancellationToken ct);
    }
}
