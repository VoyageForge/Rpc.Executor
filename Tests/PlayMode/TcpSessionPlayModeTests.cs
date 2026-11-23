using System.Collections;
using System.Net;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VoyageForge.Rpc.Executor;
using VoyageForge.Rpc.Transport;
using VoyageForge.Rpc.Transport.Session;
using VoyageForge.Rpc.Transport.Tcp;

namespace VoyageForge.Rpc.Executor.Tests.PlayMode
{
    /// <summary>
    /// PlayMode 全链路：Controller 契约 + 生成器 dispatch + RpcSession + 真实 TCP。
    /// 只测纯内存/网络，不阻塞主线程（用协程等待）。
    /// </summary>
    public sealed class TcpSessionPlayModeTests
    {
        [RpcService("session")]
        public sealed partial class TestSessionController : RpcControllerBase
        {
            [RpcMethod]
            public RpcResponse<(string SessionId, string Permissions)> Hello(string userId, string token)
            {
                return RpcResponse<(string, string)>.Ok(("sid-" + userId, "calc.add"));
            }

            [RpcMethod]
            public RpcResponse<string> Resume((string SessionId, string Tag) value)
            {
                return RpcResponse<string>.Ok(value.SessionId);
            }

            [RpcMethod]
            public RpcResponse<int> Add(int a, int b)
            {
                return Ok(a + b);
            }
        }

        private static IEnumerator WaitTask(System.Threading.Tasks.Task task, string message, float timeoutSeconds = 10f)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!task.IsCompleted && Time.realtimeSinceStartup < deadline)
                yield return null;

            Assert.IsTrue(task.IsCompleted, message);
            if (task.IsFaulted) throw task.Exception.GetBaseException();
        }

        [UnityTest]
        public IEnumerator Add_全链路返回结果()
        {
            TcpRpcServerTransport server = new TcpRpcServerTransport(
                IPAddress.Loopback, 0, new NewtonsoftRpcSerializer());
            ServiceRegistry registry = new ServiceRegistry();
                registry.Register<TestSessionController>(new TestSessionController(), "session");
                IGeneratedRpcDispatcher dispatcher = new VoyageForge.Rpc.Generated.RpcGeneratedDispatcher();
                RpcExecutor executor = new RpcExecutor(registry,
                    new GeneratedMethodResolver(dispatcher), new GeneratedRpcInvoker(dispatcher));
            server.OnRequestAsync += (request, ct) => executor.ExecuteAsync(request, ct);
            yield return WaitTask(server.StartAsync(CancellationToken.None), "服务端启动超时");

            TcpRpcClientTransport client = new TcpRpcClientTransport(
                "127.0.0.1", server.LocalEndPoint.Port, new NewtonsoftRpcSerializer());
            RpcSession session = new RpcSession(client);
            // 本用例只验证 Add 链路，跳过握手（避免 Hello 元组响应的干扰）。
            byte[] addPayload = RpcPayload.Pack((3, 4));
            Debug.Log("[PlayModeTest] addPayload len=" + addPayload.Length +
                " bytes=" + System.BitConverter.ToString(addPayload));
            var addTask = session.CallAsync<int>(new RpcRequest
            {
                ServiceName = "session",
                MethodName = "Add",
                Payload = addPayload
            }, CancellationToken.None);
            yield return WaitTask(addTask, "Add 超时");

            RpcResponse<int> response = addTask.Result;
            Debug.Log("[PlayModeTest] Add success=" + response.Success +
                " data=" + response.Data + " err=" + (response.Error?.Code ?? "none"));
            session.Dispose();
            yield return WaitTask(server.StopAsync(CancellationToken.None), "服务端停止超时");

            Assert.IsTrue(response.Success, "Add 失败：" + (response.Error?.Code + " " + response.Error?.Message));
            Assert.AreEqual(7, response.Data);
        }
    }
}