using VoyageForge.Rpc.Transport.Session;

namespace VoyageForge.Rpc.Samples
{
    /// <summary>
    /// 调用方的代理集合：属性 <c>Calculator</c> 及代理实现由 <c>ControllerRpcGenerator</c> 生成。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 属性名默认取接口名去掉前导 <c>I</c> —— <c>ICalculator</c> → <c>Calculator</c>。
    /// </para>
    /// <para>
    /// 必须是非泛型 <c>partial class</c> 且继承 <see cref="ControllerMap"/>。
    /// 调用方只持有这个 Map，不引用服务端实现。
    /// </para>
    /// </remarks>
    [ControllerMap(typeof(ICalculator))]
    public partial class CalculatorControllerMap : ControllerMap
    {
    }
}
