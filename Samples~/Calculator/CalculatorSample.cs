using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VoyageForge.Rpc.Generated.VoyageForge_Rpc_Executor_Samples;
using VoyageForge.Rpc.Transport.Session;

namespace VoyageForge.Rpc.Samples
{
    /// <summary>Sample 的组装入口，可由 MonoBehaviour、Editor 按钮或测试代码调用。</summary>
    public static class CalculatorSample
    {
        /// <summary>
        /// 构建服务端 Controller 注册表。
        /// </summary>
        /// <remarks>
        /// <c>GeneratedRpcControllerCatalog</c> 由生成器按<b>程序集</b>产出
        /// （命名空间为 <c>VoyageForge.Rpc.Generated.&lt;程序集名&gt;</c>，非标识符字符替换为下划线），
        /// 这里把本程序集所有 <c>[RpcController]</c> 注册进去。
        /// </remarks>
        public static RpcControllerRegistry CreateRegistry()
        {
            RpcControllerRegistry registry = new RpcControllerRegistry();

            // 工厂在每次调用时执行：返回新的 Controller 实例，
            // 避免 Controller 内部的可变字段被并发请求互相覆盖（不要返回共享单例）。
            GeneratedRpcControllerCatalog.Register(
                registry,
                type => type == typeof(Calculator) ? new Calculator() : null);

            return registry;
        }

        /// <summary>
        /// 演示 Controller 调用：调用端只持有 <see cref="CalculatorControllerMap"/>，
        /// 通过 <c>session.Ability.Calculator</c> 像调本地方法一样调远端。
        /// </summary>
        /// <remarks>
        /// 这里用无参构造的 <c>TcpSession</c>（不建立真实连接），只演示装配与调用形态；
        /// 真实项目应当 <c>new TcpSession&lt;CalculatorControllerMap&gt;(tcpClient, registry)</c>。
        /// 离线时调用会失败，所以整段用 try/catch 包住并降级到本地直调，
        /// 保证示例场景在没连服务端时点按钮也不会炸。
        /// </remarks>
        public static async Task RunAsync()
        {
            RpcControllerRegistry registry = CreateRegistry();
            TcpSession<CalculatorControllerMap> session = new TcpSession<CalculatorControllerMap>(registry);

            try
            {
                int sum = await session.Ability.Calculator.Add(3, 4);
                Debug.Log("[Controller] 3 + 4 = " + sum);

                // Controller 模式的 proxy 方法签名与契约接口一致，
                // 所以 CancellationToken 由调用方显式传入（框架不会自动省略这个形参）。
                double quotient = await session.Ability.Calculator.DivideAsync(10.0, 4.0, CancellationToken.None);
                Debug.Log("[Controller] 10 / 4 = " + quotient);
            }
            catch (Exception exception)
            {
                // 无连接时走这里：仍然演示业务正确性，避免示例场景刷错误日志。
                Debug.LogWarning("[Controller] 远程调用不可用（示例未建立连接）：" + exception.Message);

                Calculator local = new Calculator();
                Debug.Log("[Controller][本地直调] 3 + 4 = " + await local.Add(3, 4));
                Debug.Log("[Controller][本地直调] 10 / 4 = " + await local.DivideAsync(10.0, 4.0, CancellationToken.None));
            }
            finally
            {
                session.Dispose();
            }
        }
    }
}
