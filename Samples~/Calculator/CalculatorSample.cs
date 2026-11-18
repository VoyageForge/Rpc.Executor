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


    /// <summary>
    /// Sample 内的手写静态分发器。
    /// 它让 Sample 在尚未构建 Source Generator DLL 时也能独立编译；业务项目仍可直接使用 CodeGen 自动生成版本。
    /// </summary>
    internal sealed class CalculatorSampleDispatcher : IGeneratedRpcDispatcher
    {
        private static readonly MethodDescriptor[] s_methods =
        {
            new MethodDescriptor
            {
                ServiceName = "calc", MethodName = "Add", MethodId = 0xC0B6E2146F1C044CUL,
                Slot = 0, ParameterTypes = new[] { typeof(int), typeof(int) },
                IsAsync = false, ResultType = typeof(int)
            },
            new MethodDescriptor
            {
                ServiceName = "calc", MethodName = "DivideAsync", MethodId = 0x20ED1E3292DDA7F4UL,
                Slot = 1, ParameterTypes = new[] { typeof(double), typeof(double), typeof(CancellationToken) },
                IsAsync = true, ResultType = typeof(double)
            }
        };

        /// <summary>按服务名、方法名和不含 CancellationToken 的参数数量查找 Sample 方法。</summary>
        public bool TryResolve(string serviceName, string methodName, object[] args, out MethodDescriptor descriptor)
        {
            int rawCount = args == null ? 0 : args.Length;
            for (int i = 0; i < s_methods.Length; i++)
            {
                MethodDescriptor candidate = s_methods[i];
                int expected = candidate.ParameterTypes.Count(type => type != typeof(CancellationToken));
                if (candidate.ServiceName == serviceName && candidate.MethodName == methodName && expected == rawCount)
                {
                    descriptor = candidate;
                    return true;
                }
            }
            descriptor = null;
            return false;
        }

        /// <summary>通过固定 Slot 静态调用 Sample 的业务接口，不使用 MethodInfo.Invoke。</summary>
                /// <summary>Mirror 式：按 MethodId 命中；示例/测试不使用，返回 false。</summary>
        public bool TryResolve(ulong methodId, object[] args, out MethodDescriptor descriptor)
        {
            descriptor = null;
            return false;
        }
public Task<object> DispatchAsync(int slot, object instance, object[] boundArgs, CancellationToken ct)
        {
            ICalculator calculator = instance as ICalculator;
            if (calculator == null) throw new InvalidOperationException("服务实例不是 ICalculator。");
            switch (slot)
            {
                case 0:
                    return Task.FromResult<object>(calculator.Add((int)boundArgs[0], (int)boundArgs[1]));
                case 1:
                    return DivideAsync(calculator, boundArgs, ct);
                default:
                    throw new InvalidOperationException("未知的 Calculator Sample Slot：" + slot);
            }
        }

        /// <summary>异步分发辅助方法，将框架注入的取消令牌传入业务接口。</summary>
        private static async Task<object> DivideAsync(ICalculator calculator, object[] boundArgs, CancellationToken ct)
        {
            return await calculator.DivideAsync((double)boundArgs[0], (double)boundArgs[1], ct);
        }
    }


    /// <summary>Sample 的组装入口，可由 MonoBehaviour、Editor 按钮或测试代码调用。</summary>
    public static class CalculatorSample
    {
        /// <summary>
        /// 构建一个可执行 calc 服务的 RpcExecutor。
        /// 这里使用手写静态分发器，避免 Sample 在尚未部署生成器 DLL 时出现缺失类型。
        /// </summary>
        public static RpcExecutor CreateExecutor()
        {
            ServiceRegistry registry = new ServiceRegistry();
            registry.Register<ICalculator>(new Calculator());

            // Resolver 负责选择方法描述符；Invoker 负责按 Slot 调用。两者共用同一个静态分发器实例。
            IGeneratedRpcDispatcher dispatcher = new CalculatorSampleDispatcher();
            return new RpcExecutor(
                registry,
                new GeneratedMethodResolver(dispatcher),
                new GeneratedRpcInvoker(dispatcher));
        }

        /// <summary>执行需求文档中的两个示例：Add(3,4)=7、DivideAsync(10,4)=2.5。</summary>
        public static async Task RunAsync()
        {
            RpcExecutor executor = CreateExecutor();
            RpcResponse add = await executor.ExecuteAsync(new RpcRequest
            {
                ServiceName = "calc",
                MethodName = "Add",
                Parameters = new object[] { 3, 4 }
            });
            UnityEngine.Debug.Log("3 + 4 = " + add.Result);

            RpcResponse divide = await executor.ExecuteAsync(new RpcRequest
            {
                ServiceName = "calc",
                MethodName = "DivideAsync",
                Parameters = new object[] { 10.0, 4.0 }
            });
            UnityEngine.Debug.Log("10 / 4 = " + divide.Result);
        }
    }
}
