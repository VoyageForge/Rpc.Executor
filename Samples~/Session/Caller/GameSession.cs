using VoyageForge.Rpc.Transport;
using VoyageForge.Rpc.Samples.Contracts;
using VoyageForge.Rpc.Samples.Receiver;

namespace VoyageForge.Rpc.Samples.Caller
{
    [RpcApi(typeof(ISessionApi))]
    [RpcControllerBinding(typeof(SessionController))]
    public sealed partial class GameSession : Session
    {
    }
}
