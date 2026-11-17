# Calculator RPC Sample

## 导入和运行

1. 确认 `Rpc.Transport` 和 `Rpc.Executor` 两个 Package 已安装（Executor manifest 会声明 Transport 依赖）。
2. 在 Package Manager 中导入 `Calculator RPC 基础示例`。
3. 在临时 MonoBehaviour、Editor 按钮或测试代码中执行 `await CalculatorSample.RunAsync()`。
4. 这个 Sample 使用手写的 `CalculatorSampleDispatcher`，因此不要求先构建 Source Generator DLL；手写分发器只为 Sample 提供稳定、可运行的入口。

## 预期结果

```text
3 + 4 = 7
10 / 4 = 2.5
```

把 `DivideAsync` 的第二个参数改成 `0.0`，业务方法会出现除零异常。执行器会把异常包装成 `Success == false` 和 `Error.Code == "System.DivideByZeroException"`。

## 建议阅读顺序

1. `ICalculator`：看 `[RpcService("calc")]` 和方法签名。
2. `Calculator`：看业务实现，确认业务类没有 Roslyn 依赖。
3. `CalculatorSampleDispatcher`：看静态描述符表与 Slot switch。
4. `CalculatorSample.CreateExecutor`：理解服务注册、Resolver、Invoker 的组装。
5. `RpcExecutor.ExecuteAsync`：确认运行时依次做服务查找、重载选择、参数绑定和静态调用。

在自己的业务程序集里，仍可使用 `Rpc.Executor/CodeGen~` 的 Source Generator 自动生成同等分发代码；Sample 回退代码不替代正式生成器。
