using System.Threading;
using System.Threading.Tasks;
using VoyageForge.Rpc.Transport.Session;

namespace VoyageForge.Rpc.Samples
{
    /// <summary>
    /// Calculator 的 Controller 契约（0.3.0 起的推荐形态）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与旧的 <c>[RpcService]</c> 接口相比，Controller 契约有三处硬约束：
    /// </para>
    /// <list type="number">
    ///   <item><description>方法返回类型必须是 <c>Task</c> 或 <c>Task&lt;T&gt;</c>；
    ///   <c>T</c> 就是业务返回值，<b>不再</b>用 <c>RpcResponse&lt;T&gt;</c> 包裹。</description></item>
    ///   <item><description><c>CancellationToken</c> 最多一个，且必须是最后一个参数。</description></item>
    ///   <item><description>最多 7 个业务参数。</description></item>
    /// </list>
    /// <para>
    /// 请求参数会由 <c>ControllerRpcGenerator</c> 自动打包成 DTO
    /// （<c>CalculatorAddRequest</c> 等），<b>无需手写</b> <c>[MemoryPackable]</c> 类。
    /// </para>
    /// </remarks>
    [Controller("calc")]
    public interface ICalculator
    {
        /// <summary>加法：两个 int 参数会被自动打包成 <c>CalculatorAddRequest</c>。</summary>
        Task<int> Add(int a, int b);

        /// <summary>
        /// 浮点除法：展示 <c>CancellationToken</c> 自动注入（调用方不传它）。
        /// </summary>
        Task<double> DivideAsync(double a, double b, CancellationToken cancellationToken);
    }
}
