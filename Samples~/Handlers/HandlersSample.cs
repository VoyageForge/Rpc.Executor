using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using VoyageForge.Rpc.Runtime.Handlers;

namespace VoyageForge.Rpc.Samples.Handlers
{
    /// <summary>
    /// 强类型处理器示例（IRequestHandler / IAsyncRequestHandler）：
    /// 与字符串协议（RpcRequest/RpcResponse）不同，这里直接传请求对象、拿响应对象。
    /// 处理器目录由 RpcHandlerGenerator 在编译期生成，无需手动注册。
    /// </summary>
    public sealed class HandlersSample : MonoBehaviour
    {
        private async void Start()
        {
            // 执行器是普通实例类；需要单例时由业务层自己包一层。
            RpcExecutor executor = new RpcExecutor();

            // 1. 同步通道：只走 IRequestHandler。
            EchoResponse sync = executor.Send<EchoRequest, EchoResponse>(
                new EchoRequest { Text = "hello" });
            Debug.Log("[HandlersSample] " + sync.Text);

            // 2. 异步通道：只走 IAsyncRequestHandler，取消令牌会透传。
            EchoResponse async = await executor.SendAsync<EchoAsyncRequest, EchoResponse>(
                new EchoAsyncRequest { Text = "hello" }, CancellationToken.None);
            Debug.Log("[HandlersSample] " + async.Text);
        }
    }
}