using System.Threading;
using System.Threading.Tasks;
using System;
using System.Linq;
using VoyageForge.Rpc.Executor;
using VoyageForge.Rpc.Executor.Abstractions;
using VoyageForge.Rpc.Transport;

namespace VoyageForge.Rpc.Samples
{
    /// <summary>
    /// 一个最小 RPC 服务契约。RpcGenerator 编译期读取 [RpcService]，但不会修改这份业务源码。
    /// </summary>
    [RpcService("calc")]
    public interface ICalculator
    {
        /// <summary>同步方法：展示 int 参数和 int 返回值。</summary>
        int Add(int a, int b);

        /// <summary>
        /// 异步方法：展示 Task&lt;T&gt; 返回值解包，以及 CancellationToken 自动注入。
        /// 调用方只传两个 double，第三个 token 由 RpcExecutor 提供。
        /// </summary>
        Task<double> DivideAsync(double a, double b, CancellationToken cancellationToken);
    }

    /// <summary>纯业务实现，不依赖 Roslyn、Source Generator 或 RpcExecutor 内部实现。</summary>
    public sealed class Calculator : ICalculator
    {
        /// <summary>返回两个整数的和。</summary>
        public int Add(int a, int b) { return a + b; }

        /// <summary>计算浮点除法；分母为零时的异常由 RpcExecutor 转换为 RpcResponse.Error。</summary>
        public Task<double> DivideAsync(double a, double b, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(a / b);
        }
    }


    /// <summary>Sample 的组装入口，可由 MonoBehaviour、Editor 按钮或测试代码调用。</summary>
    public static class CalculatorSample
    {
        /// <summary>
        /// 构建一个可执行 calc 服务的 RpcExecutor。
        /// 分发器由 RpcGenerator 生成（Rpc.Executor.CodeGen），Sample 不再手写。
        /// </summary>
        public static RpcExecutor CreateExecutor()
        {
            ServiceRegistry registry = new ServiceRegistry();
            registry.Register<ICalculator>(new Calculator());

            // 生成器产物：描述符表 + switch(slot) 调用；两个适配器共用同一实例。
            IGeneratedRpcDispatcher dispatcher = new VoyageForge.Rpc.Generated.RpcGeneratedDispatcher();
            return new RpcExecutor(
                registry,
                new GeneratedMethodResolver(dispatcher),
                new GeneratedRpcInvoker(dispatcher))
            {
                // 生成器名称表：MethodId → "calc.Add"，仅用于诊断，不参与网络传输。
                MethodNameResolver = VoyageForge.Rpc.Generated.RpcMethodNames.TryGetName
            };
        }

        /// <summary>执行需求文档中的两个示例：Add(3,4)=7、DivideAsync(10,4)=2.5。</summary>
        public static async Task RunAsync()
        {
            RpcExecutor executor = CreateExecutor();

            // 客户端只给 ServiceName + MethodName；MethodId 由 RpcExecutor 计算并缓存。
            RpcResponse<int> add = await executor.ExecuteAsync<int>(new RpcRequest
            {
                ServiceName = "calc",
                MethodName = "Add",
                Payload = RpcPayload.Pack(new object[] { 3, 4 })
            });
            UnityEngine.Debug.Log("3 + 4 = " + add.Data);

            RpcResponse<double> divide = await executor.ExecuteAsync<double>(new RpcRequest
            {
                ServiceName = "calc",
                MethodName = "DivideAsync",
                Payload = RpcPayload.Pack(new object[] { 10.0, 4.0 })
            });
            UnityEngine.Debug.Log("10 / 4 = " + divide.Data);
        }    }
}
