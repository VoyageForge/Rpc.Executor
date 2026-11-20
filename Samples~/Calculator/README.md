# Calculator RPC Sample

演示现代 RPC 写法：契约 → 生成器分发/代理 → `ServicesMap` 调用。

## 依赖

- `com.voyageforge.rpc-executor`（含 `com.voyageforge.rpc-transport`）；
- Unity 侧 `MemoryPack.Core`（见根 README）；
- 生成器 DLL `Runtime/Analyzers/Rpc.Executor.CodeGen.dll`（Label `RoslynAnalyzer`，首次使用前重启 Unity）。

## 运行

1. 导入本示例（软链接或 Package Manager）；
2. 运行 `CalculatorScene`，或执行：

```csharp
await CalculatorSample.RunAsync();
```

## 示例契约

```csharp
[RpcService("calc")]
public interface ICalculator
{
    RpcResponse<int> Add(int a, int b);
    Task<RpcResponse<double>> DivideAsync(double a, double b, CancellationToken cancellationToken);
}
```

- 返回值必须 `RpcResponse` 包裹；
- 业务实现只填 `Data`，`Success` 由生成器补；
- `CancellationToken` 由框架注入，不占请求参数。

## 调用端

```csharp
ServicesMap api = new ServicesMap(CreateExecutor());

RpcResponse<int> add = await api.Add(3, 4);          // 2 参数 → 元组
RpcResponse<double> divide = await api.DivideAsync(10.0, 4.0);  // 1 参数 → 直接序列化

if (!divide.Success)
    Debug.LogError(divide.Error.Code + ": " + divide.Error.Message);
else
    Debug.Log("10 / 4 = " + divide.Data);
```

`api.Add` / `api.DivideAsync` 是生成器产物 `RpcGeneratedClients.g.cs` 里的 `ServicesMap` 扩展方法，底层直接 `RpcPayload.Pack(...)`（无 `object[]`、无装箱）。

## 预期结果

```text
3 + 4 = 7
10 / 4 = 2.5
```

把 `DivideAsync(10.0, 0.0)` 传 0 时，契约返回的 `RpcResponse<double>.Success` 为 true、`Data` 为 `Infinity`（`double` 除零不抛异常）；如果业务主动 `throw`，执行器会包装成 `Error.Code == "System.DivideByZeroException"`。

## 阅读顺序

1. `CalculatorSample.cs`：契约、业务实现、`CreateExecutor`、`RunAsync`；
2. `ServicesMapSample.cs`：`ClientServicesMap : ServicesMap` 的强类型封装；
3. 生成产物 `RpcGeneratedDispatcher.g.cs` / `RpcGeneratedClients.g.cs`（Unity 里 F12 查看）。