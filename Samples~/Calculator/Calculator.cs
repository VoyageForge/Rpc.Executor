using System;
using System.Threading;
using System.Threading.Tasks;
using VoyageForge.Rpc.Executor;

namespace VoyageForge.Rpc.Samples
{
    /// <summary>
    /// Calculator 的服务端业务实现。Dispatcher 由 <c>ControllerRpcGenerator</c> 生成到本类的 partial 部分。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 必须同时满足：<c>[RpcController(typeof(ICalculator))]</c>、非抽象非泛型、
    /// <c>partial class</c>、继承 <see cref="RpcControllerBase"/>、实现契约接口。
    /// </para>
    /// <para>
    /// <b>为什么必须 partial</b>：生成器要把 dispatch 方法写进这个类的另一个 partial 部分，
    /// 而不是另造一个包装类。
    /// </para>
    /// <para>
    /// 业务方法直接返回业务值；失败抛异常（框架转成错误响应），
    /// 或继承基类的 <c>Fail</c> 语义自行包装。
    /// </para>
    /// </remarks>
    [RpcController(typeof(ICalculator))]
    public sealed partial class Calculator : RpcControllerBase, ICalculator
    {
        /// <summary>返回两个整数的和。</summary>
        public Task<int> Add(int a, int b)
        {
            return Task.FromResult(a + b);
        }

        /// <summary>浮点除法；取消令牌由框架注入。</summary>
        public Task<double> DivideAsync(double a, double b, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (b == 0)
            {
                throw new DivideByZeroException("除数不能为 0。");
            }

            return Task.FromResult(a / b);
        }
    }
}
