# VoyageForge RPC Executor

传输无关的 RPC 执行器（`com.voyageforge.rpc-executor`），目标 Unity 2022.3。

## 职责

- **字符串协议执行器**：`RpcExecutor.ExecuteAsync(RpcRequest)`，经 `ServiceRegistry` → `IMethodResolver` → `IArgumentConverter` → `IInvoker` 执行生成/手写的分发代码；
- **强类型执行器**：`VoyageForge.Rpc.Runtime.Handlers.RpcExecutor` 的 `Send` / `SendAsync`，通过 `IRpcHandlerCatalog` 调用 `IRequestHandler` / `IAsyncRequestHandler`；
- **幂等缓存**：默认开启，按 `RequestId` 去重成功响应（5 分钟 TTL，可配置时长/容量，不提供关闭开关）；
- **消息编号**：`MessageId` / `[RpcMessage]` / `RpcMessageTypeRegistry`，以及源生成器 `RpcMessageTypeIdGenerator` 生成 `RpcMessageIds` / `RpcMessageTypeMap`；
- **源生成器**：`RpcGenerator`（字符串协议分发）、`RpcHandlerGenerator`（强类型处理器目录）。

它不关心请求来自 Console、TCP 还是测试代码。

## 字符串协议执行器

```csharp
ServiceRegistry services = new ServiceRegistry();
services.Register<ICalculator>(new Calculator(), "calc");

RpcExecutor executor = new RpcExecutor(
    services,
    new GeneratedMethodResolver(dispatcher),
    new GeneratedRpcInvoker(dispatcher));

RpcResponse response = await executor.ExecuteAsync(new RpcRequest
{
    ServiceName = "calc",
    MethodName = "Add",
    Parameters = new object[] { 1, 2 }
}, ct);
```

处理的错误码：

- `SERVICE_NOT_FOUND`、`METHOD_NOT_FOUND`、`METHOD_AMBIGUOUS`、`ARGUMENT_BIND_ERROR`、`CANCELLED`；
- 业务异常：`Error.Code` 为异常类型全名（`ExposeExceptionDetail` 控制是否返回堆栈）。

拦截器按注册顺序组成洋葱模型；拦截器异常同样转换为 `RpcResponse`。

## 幂等缓存

`IdempotencyCache`（`Rpc.Executor`）：

- 默认 TTL 5 分钟，默认容量 1024；
- 只缓存**成功**响应；失败不缓存，客户端可用新 `RequestId` 重试；
- `RequestId == 0` 的本地调用不参与；
- 执行器构造可注入（`new RpcExecutor(..., idempotency: cache)`），默认自动创建。

## MessageId 与生成器

- `[RpcMessage(1001)]` 显式编号；未标注类型由生成器从 `1000` 自动编号（按类型全名排序，保证稳定）；
- 运行时兜底：`RpcMessageTypeRegistry.Register(Type, MessageId?)` / `TryGetMessageId` / `TryGetType`；
- 生成器产物：`RpcMessageIds`（`static readonly MessageId`）+ `RpcMessageTypeMap.TryGetMessageId/TryGetType`。

## 源代码生成器

三个生成器都在 `CodeGen~/Rpc.Executor.CodeGen`，产物是普通 C# 源码：

| 生成器 | 产物 | 内容 |
|---|---|---|
| `RpcGenerator` | `RpcGeneratedDispatcher.g.cs` | `RpcMethodIds` 常量、描述符数组、重载解析、Slot switch |
| `RpcHandlerGenerator` | `RpcHandlerCatalog.g.cs` | 程序集级 `[RpcHandlerCatalog]` + 请求类型 → 处理器目录 |
| `RpcMessageTypeIdGenerator` | `RpcMessageIds.g.cs` | `RpcMessageIds` + `RpcMessageTypeMap` |

详细说明见 `Documentation/源代码生成器.md`。

## 构建生成器

`CodeGen~` 是独立 Roslyn 项目（不参与 UPM 导入）：

```powershell
.\Build-CodeGen.ps1
```

构建脚本会把 DLL 部署到 `Runtime/Analyzers/Rpc.Executor.CodeGen.dll`，并生成 `.meta`。请在 Unity Inspector 确认该 DLL 的 Asset Label 为 `RoslynAnalyzer`。

Roslyn 版本：`Microsoft.CodeAnalysis.CSharp` 4.1.0（Unity 2022.3 的 Roslyn 4.2 兼容 Incremental Generator），`LangVersion` 9.0。

## 测试

- `Tests/EditMode`：
  - `ServiceRegistryTests`：名称解析/覆盖/重复策略/null；
  - `JsonArgumentConverterTests`：基础类型/枚举/Guid/TimeSpan/null/失败；
  - `Fnv1A64MethodIdProviderTests`：稳定性/敏感性/参数校验；
  - `RpcExecutorTests`：成功、错误码、取消、拦截器顺序/短路；
  - `GeneratedContractTests`：`GeneratedMethodResolver` / `GeneratedRpcInvoker`；
  - `IdempotencyCacheTests` / `ExecutorIdempotencyTests`：TTL、成功缓存、同 Id 只执行一次、失败重试；
  - `HandlersRpcExecutorTests`：强类型 `Send`/`SendAsync`、通道不匹配、未注册类型。

`testables` 中启用本包：

```json
{
  "testables": [
    "com.voyageforge.rpc-executor"
  ]
}
```

## 安装

通过 Package Manager 的 Add package from disk 选择本包 `package.json`；依赖 `com.voyageforge.rpc-transport` 与 `com.unity.nuget.newtonsoft-json`。