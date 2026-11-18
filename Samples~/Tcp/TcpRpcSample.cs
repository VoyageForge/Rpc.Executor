using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using VoyageForge.Rpc.Executor;
using VoyageForge.Rpc.Executor.Abstractions;
using VoyageForge.Rpc.Transport;
using VoyageForge.Rpc.Transport.Tcp;

namespace VoyageForge.Rpc.Samples.Tcp
{
    /// <summary>TCP Sample 自己的 RPC 服务契约，独立于 Calculator 基础 Sample 程序集。</summary>
    [RpcService("tcp.calc")]
    public interface ITcpCalculator
    {
        /// <summary>返回两个整数的和。</summary>
        int Add(int a, int b);
    }

    /// <summary>TCP Sample 的业务实现；它不引用 TCP 类型。</summary>
    public sealed class TcpCalculator : ITcpCalculator
    {
        /// <summary>加法业务逻辑。</summary>
        public int Add(int a, int b) { return a + b; }
    }


    /// <summary>
    /// TCP Sample 的手写静态分发器，避免示例程序集依赖尚未构建的 Source Generator DLL。
    /// TCP 传输只传消息，不关心这个分发器是手写还是编译期生成。
    /// </summary>
    internal sealed class TcpSampleDispatcher : IGeneratedRpcDispatcher
    {
        private static readonly MethodDescriptor s_method = new MethodDescriptor
        {
            ServiceName = "tcp.calc", MethodName = "Add", MethodId = 0xB35219B64DFD3BB3UL,
            Slot = 0, ParameterTypes = new[] { typeof(int), typeof(int) },
            IsAsync = false, ResultType = typeof(int)
        };

        /// <summary>只接受 tcp.calc.Add(int,int) 这一条 Sample 方法。</summary>
        public bool TryResolve(string serviceName, string methodName, object[] args, out MethodDescriptor descriptor)
        {
            if (serviceName == s_method.ServiceName && methodName == s_method.MethodName &&
                args != null && args.Length == 2)
            {
                descriptor = s_method;
                return true;
            }
            descriptor = null;
            return false;
        }

        /// <summary>按 Slot 静态调用 TCP Sample 的业务接口。</summary>
        public Task<object> DispatchAsync(int slot, object instance, object[] boundArgs, CancellationToken ct)
        {
            if (slot != 0) throw new InvalidOperationException("未知的 TCP Sample Slot：" + slot);
            ITcpCalculator calculator = instance as ITcpCalculator;
            if (calculator == null) throw new InvalidOperationException("服务实例不是 ITcpCalculator。");
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<object>(calculator.Add((int)boundArgs[0], (int)boundArgs[1]));
        }
    }

    /// <summary>
    /// TCP + RPC 的完整组装示例.
    /// 注意：服务端只负责把收到的 RpcRequest 转交给 RpcExecutor；客户端只通过 IRpcClientTransport 发送请求。
    /// 业务服务、序列化器和 TCP Socket 三者互不直接依赖。
    /// </summary>
    public static class TcpRpcSample
    {
        /// <summary>参考监听端口；实际项目应从配置读取，避免硬编码端口冲突。</summary>
        public const int Port = 17860;

        /// <summary>创建本 Sample 的 RPC 执行器，与 Calculator 基础 Sample 保持程序集独立。</summary>
        private static RpcExecutor CreateExecutor()
        {
            ServiceRegistry registry = new ServiceRegistry();
            registry.Register<ITcpCalculator>(new TcpCalculator());

            // 本 Sample 使用静态分发器；业务项目可替换为 Source Generator 产物，无需反射扫描业务方法。
            IGeneratedRpcDispatcher dispatcher = new TcpSampleDispatcher();
            return new RpcExecutor(registry,
                new GeneratedMethodResolver(dispatcher),
                new GeneratedRpcInvoker(dispatcher));
        }

        /// <summary>
        /// 启动 TCP RPC 服务端。
        /// 可从 MonoBehaviour.Start 中调用；正式项目建议由专门的网络 Host 管理生命周期。
        /// </summary>
        public static async Task<TcpRpcServerTransport> StartServerAsync(CancellationToken ct)
        {
            // 1. 先创建 RPC 执行器并注册业务服务。它本身完全不知道 TCP 的存在。
            RpcExecutor executor = CreateExecutor();

            // 2. 创建序列化器与 TCP 传输。IPAddress.Any 表示监听本机所有 IPv4 网卡。
            NewtonsoftRpcSerializer serializer = new VoyageForge.Rpc.Transport.NewtonsoftRpcSerializer();
            TcpRpcServerTransport server = new TcpRpcServerTransport(IPAddress.Any, Port, serializer);

            // 3. 将网络收到的请求委托给 RPC 执行器。
            //    服务端停止时 TCP Server 会取消 requestToken；Executor 会把它注入业务 CancellationToken。
            //    注意：TCP 对端在业务执行期间断开，不一定能立即被服务端感知。
            server.OnRequestAsync += (request, requestToken) => executor.ExecuteAsync(request, requestToken);
            server.OnTransportError += exception => UnityEngine.Debug.LogException(exception);

            // 4. 开始监听。StartAsync 完成代表监听器已启动，不代表某个客户端已经连接。
            await server.StartAsync(ct);
            return server;
        }

        /// <summary>
        /// 通过 TCP 调用远端 tcp.calc.Add(3, 4)。调用方只面对 IRpcClientTransport 接口。
        /// </summary>
        public static async Task<int> CallAddAsync(string host, CancellationToken ct)
        {
            IRpcClientTransport client = new TcpRpcClientTransport(host, Port, new VoyageForge.Rpc.Transport.NewtonsoftRpcSerializer());
            RpcResponse response = await client.SendAsync(new RpcRequest
            {
                ServiceName = "tcp.calc",
                MethodName = "Add",
                Parameters = new object[] { 3, 4 }
            }, ct);

            if (!response.Success)
                throw new InvalidOperationException("RPC 调用失败 [" + response.Error.Code + "]：" + response.Error.Message);

            return Convert.ToInt32(response.Result, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 最小联调流程：在同一进程启动服务端，再通过 TCP 客户端请求本机服务。
        /// 实际使用时服务端和客户端通常位于不同设备/进程。
        /// </summary>
        public static async Task RunLoopbackAsync(CancellationToken ct)
        {
            TcpRpcServerTransport server = await StartServerAsync(ct);
            try
            {
                int result = await CallAddAsync("127.0.0.1", ct);
                UnityEngine.Debug.Log("TCP RPC: 3 + 4 = " + result);
            }
            finally
            {
                await server.StopAsync(CancellationToken.None);
            }
        }
    }
}
