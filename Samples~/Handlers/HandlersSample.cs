using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VoyageForge.Rpc.Runtime.Handlers;

namespace VoyageForge.Rpc.Samples.Handlers
{
    /// <summary>
    /// 强类型处理器示例（IRequestHandler / IAsyncRequestHandler）：
    /// 处理器目录由 RpcHandlerGenerator 在编译期生成，无需手动注册。
    /// 这里直接演示处理器本身；接入 RpcExecutor 时由生成的目录按请求类型分发。
    /// </summary>
    public sealed class HandlersSample : MonoBehaviour
    {
        private async void Start()
        {
            // 1. 同步处理器：直接调用，不经过字符串协议。
            IRequestHandler<EchoRequest, EchoResponse> sync = new EchoHandler();
            EchoResponse syncResponse = sync.Handle(new EchoRequest { Text = "hello" });
            Debug.Log("[HandlersSample] " + syncResponse.Text);

            // 2. 异步处理器：取消令牌透传。
            IAsyncRequestHandler<EchoAsyncRequest, EchoResponse> asyncHandler = new EchoAsyncHandler();
            EchoResponse asyncResponse = await asyncHandler.HandleAsync(
                new EchoAsyncRequest { Text = "hello" }, CancellationToken.None);
            Debug.Log("[HandlersSample] " + asyncResponse.Text);
        }
    }
}