# VoyageForge RPC Executor

传输无关的 RPC 执行器（`com.voyageforge.rpc-executor`），目标 Unity 2022.3。
内部所有收发统一走 **MemoryPack**，不走 JSON、不做反射绑定。

## 核心模型

- **契约**：`[RpcService]` 标注的 interface / class，方法返回值**必须用 `RpcResponse` 包裹**；
- **调用端**：`ServicesMap` + 生成器生成的强类型代理（`api.Add(3, 4)`）；
- **服务端**：`RpcExecutor` + 生成的 `RpcGeneratedDispatcher`；
- **参数规则**：0 参数不传 Payload；1 参数直接序列化参数类型；2+ 参数自动封装为 `ValueTuple`；
- **零装箱**：调用端 `RpcPayload.Pack(...)` 泛型直写；服务端 dispatch 返回 `byte[]`。

## 写契约

```csharp
using VoyageForge.Rpc.Executor;
using VoyageForge.Rpc.Transport;

[RpcService("user")]
public interface IUserService
{
    // 同步方法
    RpcResponse<string> GetName(int userId);

    // 异步方法（CancellationToken 由框架注入，不占请求参数）
    Task<RpcResponse<UserInfo>> UpdateUser(string id, int level, CancellationToken ct);

    // 无参数
    RpcResponse Ping();

    // 无返回值
    Task RemoveUser(int userId);
}
```

规则：

1. 返回值必须是 `RpcResponse<T>`（同步）或 `Task<RpcResponse<T>>`（异步）；`RpcResponse`（无泛型）/`Task`/`void` 表示无数据；
2. 方法不能是 `static` / 泛型 / `ref` / `out` / `in` / 指针 / `ValueTask` / `async void`；
3. 业务实现**只填必要数据**（`Data`），`Success` 由生成器补：

```csharp
public sealed class UserService : IUserService
{
    public RpcResponse<string> GetName(int userId) => new RpcResponse<string> { Data = "user-" + userId };

    public async Task<RpcResponse<UserInfo>> UpdateUser(string id, int level, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return new RpcResponse<UserInfo> { Data = new UserInfo { Id = id, Level = level } };
    }

    public RpcResponse Ping() => new RpcResponse();
    public Task RemoveUser(int userId) => Task.CompletedTask;
}
```

## 调用端

```csharp
ServiceRegistry registry = new ServiceRegistry();
registry.Register<IUserService>(new UserService(), "user");

IGeneratedRpcDispatcher dispatcher = new RpcGeneratedDispatcher();
RpcExecutor executor = new RpcExecutor(
    registry,
    new GeneratedMethodResolver(dispatcher),
    new GeneratedRpcInvoker(dispatcher));

ServicesMap api = new ServicesMap(executor);

// 0 参数
RpcResponse ping = await api.Ping();
// 1 参数
RpcResponse<string> name = await api.GetName(7);
// 2+ 参数（元组）
RpcResponse<UserInfo> info = await api.UpdateUser("u1", 3);
// 无返回值
await api.RemoveUser(7);

if (!name.Success)
    Debug.LogError(name.Error.Code + ": " + name.Error.Message);
else
    Debug.Log(name.Data);
```

也可以用 `ServiceApi` 命名空间：

```csharp
ServiceApi user = api.Service("user");
RpcResponse<string> name = await user.CallAsync<string>("GetName", RpcPayload.Pack(7));
```

> 生成器代理固定返回 `Task<RpcResponse<T>>`，调用方必须先判断 `Success` 再读 `Data`。

## 服务端执行

```csharp
RpcResponse response = await executor.ExecuteAsync(new RpcRequest
{
    ServiceName = "user",
    MethodName = "GetName",
    Payload = RpcPayload.Pack(7)   // 单参数直接序列化
}, ct);
```

- `MethodId != 0` 时按 MethodId 解析（Mirror 式）；为 0 时按 `ServiceName + MethodName` 回退；
- 生成器 dispatch 内部反序列化参数并直接调用业务方法，返回值序列化进 `RpcResponse.Payload`；
- 错误码：`SERVICE_NOT_FOUND`、`METHOD_NOT_FOUND`、`METHOD_AMBIGUOUS`、`CANCELLED`；业务异常用异常类型全名作 `Error.Code`。

## 强类型处理器（可选通道）

除字符串协议外，`VoyageForge.Rpc.Runtime.Handlers.RpcExecutor` 支持 `IRequestHandler` / `IAsyncRequestHandler` 目录（`RpcHandlerGenerator` 生成）：

```csharp
EchoResponse sync = executor.Send<EchoRequest, EchoResponse>(new EchoRequest { Text = "hello" });
EchoResponse async = await executor.SendAsync<EchoAsyncRequest, EchoResponse>(new EchoAsyncRequest(), ct);
```

## 幂等缓存

- 默认 TTL 5 分钟、容量 1024；
- 只缓存**成功**响应；失败可用新 `RequestId` 重试；
- `RequestId == 0` 的本地调用不参与。

## 源代码生成器

见 `Documentation/源代码生成器.md`。产物：

| 生成器 | 产物 |
|---|---|
| `RpcGenerator` | `RpcGeneratedDispatcher.g.cs`（MethodId、描述符、重载解析、Slot switch、`Task<byte[]>`） |
| `RpcGenerator` | `RpcGeneratedClients.g.cs`（`ServicesMap` 强类型代理扩展） |
| `RpcHandlerGenerator` | `RpcHandlerCatalog.g.cs`（强类型处理器目录） |
| `RpcMessageTypeIdGenerator` | `RpcMessageIds.g.cs`（消息编号映射） |

## 构建生成器

```powershell
.\Build-CodeGen.ps1
```

DLL 部署到 `Runtime/Analyzers/Rpc.Executor.CodeGen.dll`，Asset Label 必须是 `RoslynAnalyzer`。改动生成器源码后需要 **重启 Unity** 才会重新加载。

## 测试

```json
{ "testables": ["com.voyageforge.rpc-executor"] }
```

- `Tests/EditMode`：`ServiceRegistryTests`、`JsonArgumentConverterTests`、`Fnv1A64MethodIdProviderTests`、`RpcExecutorTests`、`GeneratedContractTests`、`ServicesMapEndToEndTests`、`IdempotencyCacheTests`、`ExecutorIdempotencyTests`、`HandlersRpcExecutorTests`。

## 安装

Package Manager → Add package from disk 选择本包 `package.json`；依赖 `com.voyageforge.rpc-transport`、`com.unity.nuget.newtonsoft-json`，Unity 侧需安装 `MemoryPack.Core`（见根 README）。