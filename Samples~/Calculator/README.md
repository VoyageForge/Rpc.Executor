# Calculator RPC Sample（Controller 模式）

演示 0.3.0 的 **Controller RPC**：契约 → 生成器自动产出请求 DTO / 代理 / 分发器 → 调用端像调本地方法。

## 依赖

- `com.voyageforge.rpc-executor`（含 `com.voyageforge.rpc-transport`）；
- Unity 侧 `MemoryPack.Core`（见根 README）；
- 生成器 DLL `Runtime/Analyzers/Rpc.Executor.CodeGen.dll`（Label `RoslynAnalyzer`）。

> **改完生成器源码必须重启 Unity**，analyzer 不热更新。
> 如果改了 `Samples~` 里的源码后看不到重新编译，删掉
> `Library/ScriptAssemblies/VoyageForge.Rpc.Executor.Samples.dll` 再 refresh ——
> 软链接目录的文件监控是关闭的。

## 运行

1. 导入本示例（软链接或 Package Manager）；
2. 运行 `CalculatorScene`，或执行 `await CalculatorSample.RunAsync();`。

离线时远程调用会失败，`RunAsync` 已用 try/catch 降级到本地直调，所以点按钮不会报错。

## 契约

```csharp
[Controller("calc")]
public interface ICalculator
{
    Task<int> Add(int a, int b);
    Task<double> DivideAsync(double a, double b, CancellationToken cancellationToken);
}
```

Controller 契约的硬约束：

| 约束 | 说明 |
|---|---|
| 返回 `Task` / `Task<T>` | `T` 就是业务值，**不再**用 `RpcResponse<T>` 包裹 |
| `CancellationToken` 最多一个且最后 | 但**调用端要显式传**（代理签名与接口一致，框架不省略形参） |
| 最多 7 个业务参数 | 超出的要自己包成一个 DTO 类型 |

## 服务端

```csharp
[RpcController(typeof(ICalculator))]
public sealed partial class Calculator : RpcControllerBase, ICalculator
{
    public Task<int> Add(int a, int b) => Task.FromResult(a + b);
    public Task<double> DivideAsync(double a, double b, CancellationToken ct) { ... }
}
```

必须：非抽象非泛型、`partial class`、继承 `RpcControllerBase`、实现契约接口。
分发方法由生成器写进这个类的**另一个 partial 部分**。

装配：

```csharp
RpcControllerRegistry registry = new RpcControllerRegistry();
GeneratedRpcControllerCatalog.Register(registry, type => type == typeof(Calculator) ? new Calculator() : null);
```

`GeneratedRpcControllerCatalog` 的命名空间是 `VoyageForge.Rpc.Generated.<程序集名>`（非标识符字符换成下划线）。
工厂每次都返回新实例 —— 不要返回共享单例，否则 Controller 的可变字段会被并发请求互相覆盖。

## 调用端

```csharp
[ControllerMap(typeof(ICalculator))]
public partial class CalculatorControllerMap : ControllerMap { }

TcpSession<CalculatorControllerMap> session = new TcpSession<CalculatorControllerMap>(tcpClient, registry);

int sum = await session.Ability.Calculator.Add(3, 4);
```

属性名 `Calculator` 默认由接口名去掉前导 `I` 得到。调用端只持有 Map，**不引用**服务端实现。

## 请求 DTO 是自动生成的

`Add(int a, int b)` 的参数会被打包成：

```csharp
namespace VoyageForge.Rpc.Generated.Dto
{
    [MemoryPack.MemoryPackable]
    public partial class CalculatorAddRequest
    {
        public CalculatorAddRequest() { }
        [MemoryPack.MemoryPackOrder(0)] public int A { get; set; }
        [MemoryPack.MemoryPackOrder(1)] public int B { get; set; }
    }
}
```

外加一份 `CalculatorAddRequest.MemoryPack.g.cs`（序列化实现）。

**你不需要手写 `[MemoryPackable]` 请求类** —— 生成器在内存里构造了一个临时 Compilation，
直接驱动 MemoryPack 的 `TypeMeta.Validate()/Emit()` 产出序列化代码。
属性按**参数名序数排序**，保证调用端与服务端各自生成时布局一致。

## 与旧协议对比

| | Controller（本示例） | 旧 `[RpcService]` + `ServicesMap` |
|---|---|---|
| 契约 | `[Controller]` 接口 | `[RpcService]` 接口/类 |
| 返回类型 | `Task<T>` | `RpcResponse<T>` |
| 请求载荷 | 自动生成 DTO | ValueTuple / 单参数直序列化 |
| 调用端 | `ControllerMap` 代理（`session.Ability.Xxx`） | `ServicesMap` 扩展方法（字符串 service/method） |
| 需要手写 DTO | 不需要 | 2+ 参数时用元组，复杂类型需自己写 |

`ServicesMapSample.cs` 保留了旧协议的写法（`UserController` + `ClientServicesMap`）用于对照。**新代码建议用 Controller。**

## 文件阅读顺序

1. `ICalculator.cs` —— Controller 契约；
2. `Calculator.cs` —— 服务端实现；
3. `CalculatorControllerMap.cs` —— 调用端代理集合；
4. `CalculatorSample.cs` —— 装配与调用演示；
5. `ServicesMapSample.cs` —— 旧协议对照。

## 预期结果

```text
[Controller] 3 + 4 = 7
[Controller] 10 / 4 = 2.5
```

`DivideAsync(10.0, 0.0)` 会抛 `DivideByZeroException`，由框架转成错误响应。
