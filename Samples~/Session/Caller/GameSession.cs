using VoyageForge.Rpc.Samples.Contracts;
using VoyageForge.Rpc.Transport.Session;

namespace VoyageForge.Rpc.Samples.Caller
{
    /// <summary>调用方只声明 Session 拥有哪些远端 API；属性、代理和 Create() 均由生成器生成。</summary>
    [RpcSession]
    [RpcApi(typeof(ISessionApi))]
    public partial class GameSession : Session
    {
    }
}
