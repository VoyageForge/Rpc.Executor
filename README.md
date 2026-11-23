# VoyageForge RPC Executor

Unity 2022.3 RPC 执行与 Controller 源代码生成包：`com.voyageforge.rpc-executor`。

## Controller RPC（与 TcpSession 配合）

`Rpc.Executor` 提供服务端 `RpcControllerBase` 和 `RpcControllerAttribute`。`ControllerRpcGenerator` 扫描共享契约、调用方 `ControllerMap` 和服务端 Controller：

- `[Controller("User")] interface IUserController`：共享方法契约；
- `[ControllerMap(typeof(IUserController))] partial class GameControllerMap`：调用方代理入口；
- `[RpcController(typeof(IUserController))] partial class UserController : RpcControllerBase, IUserController`：被调用方业务实现；
- 每个包含服务端 Controller 的程序集生成独立 `GeneratedRpcControllerCatalog`；
- 应用启动时将 Catalog 注册到 `RpcControllerRegistry`，之后共享给多条 Session。

```csharp
[Controller("User")]
public interface IUserController
{
    Task<UserInfo> GetUser(GetUserRequest request, CancellationToken ct);
}

[RpcController(typeof(IUserController))]
public sealed partial class UserController : RpcControllerBase, IUserController
{
    public Task<UserInfo> GetUser(GetUserRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new UserInfo { Id = request.Id });
    }
}
```

Controller 必须为非泛型、非抽象 `partial class`。契约只支持 `Task` / `Task<T>`，最多 7 个业务参数；`CancellationToken` 可选且必须是最后一个参数。业务 DTO 必须在用户源码中使用 `[MemoryPackable] partial`，由 MemoryPack 自己的 Generator 生成序列化实现。

`GeneratedRpcControllerCatalog.Register(registry, controllerFactory)` 的 factory 在每次 RPC 调用时被执行。请返回新实例或请求作用域实例，不要返回共享 Controller 单例，以免并发请求共享可变状态。

## MethodId

FNV-1a 64，规范化 Key 包含 Controller Name、Method Name、全部业务参数类型规范名（按声明顺序）。协议版本为 `v2`。`CancellationToken` 不参与 MethodId。网络层只传 MethodId，不传名称；名称和签名留在 Catalog 中用于诊断。

## 旧执行器通道

原 `[RpcService]` / `ServicesMap` 字符串协议和 `IRequestHandler` / `IAsyncRequestHandler` 强类型 Handler 通道暂时保留，供现有项目迁移使用。它们与 `TcpSession<TControllerMap>` Controller RPC 独立，不要在同一组 Controller 契约中混用两种生成模型。

## 构建 Source Generator

```powershell
.\Build-CodeGen.ps1
```

输出部署到 `Runtime/Analyzers/Rpc.Executor.CodeGen.dll`，Unity Analyzer Label 为 `RoslynAnalyzer`。修改后重启 Unity。
