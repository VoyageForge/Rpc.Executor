# VoyageForge RPC Executor

Unity 2022.3 RPC 源代码生成与本地 Controller 执行包：`com.voyageforge.rpc-executor`。

## 核心模型

用户只编写两类代码：

1. 调用方声明 RPC 契约接口，并在自己的 `Session` 子类上选择要调用的远端 API。
2. 被调用方编写本地 Controller；它不需要引用调用方的接口。

```csharp
// 调用方：只声明协议和 Session 所拥有的 API。
[RpcContract("Session")]
public interface ISessionApi
{
    Task<string> ConnectAsync(string userId, string token, CancellationToken ct);
}

[RpcSession]
[RpcApi(typeof(ISessionApi))]
public partial class GameSession : Session { }

// 被调用方：独立声明本地执行方法，不实现 ISessionApi。
[RpcController("Session")]
public partial class SessionController : RpcControllerBase
{
    [RpcMethod]
    public Task<string> ConnectAsync(string account, string credential, CancellationToken ct)
    {
        // 当前请求对应的 Session 可通过 Session 属性访问。
        return Task.FromResult("Connected: " + account);
    }
}
```

`[RpcSession]` 会生成只读 API 属性、私有只读代理字段、私有构造函数及单一 `Create(TcpClient client = null, RpcControllerRegistry registry = null, ...)` 工厂。使用方式：

```csharp
RpcControllerRegistry registry = GeneratedRpcControllerCatalog.CreateRegistry(
    type => new SessionController());
GameSession session = GameSession.Create(tcpClient, registry);
string result = await session.SessionApi.ConnectAsync(userId, token, ct);
```

用户不需要 `new Session`、创建 Proxy、创建 DTO 或手动做参数打包。不同 Session 类型可以声明不同的 `[RpcApi]`，每个实例拥有自己的代理和 Blackboard。业务程序集需要引用 `MemoryPack.Core`，生成代码会直接调用 `MemoryPackSerializer`。

## 双端协议约定

- API 接口使用 `[RpcContract("名称")]` 声明协议 Controller 名称。
- 本地 Controller 使用 `[RpcController("名称")]` 声明相同名称。
- 接口方法与本地方法使用相同的方法协议名；本地方法通过 `[RpcMethod]` 显式暴露，可用 `[RpcMethod("协议方法名")]` 自定义名称。
- 参数类型与声明顺序必须一致，返回类型也必须一致；参数变量名称可以不同。
- 只支持 `Task` / `Task<T>`，最多 7 个业务参数；`CancellationToken` 可选且必须是最后一个参数。
- `CancellationToken` 不进入 DTO 和 MethodId；本地取消会通过 `RpcCancel` 通知对端，远端 Controller 收到取消后可通过自己的 `CancellationToken` 协作终止执行。
- DTO 是生成器内部的 `internal` 类型，使用 `Arg0`、`Arg1` 等位置字段，不要求两端参数名称相同。
- 0 个业务参数使用空 Payload；有参数时由生成器生成 MemoryPack DTO 和序列化实现。

MethodId 使用 FNV-1a 64，协议 Key 包含版本、Controller 名称、方法名和按声明顺序排列的业务参数类型规范名。不要仅通过更改方法参数顺序来做无兼容升级；协议变更应按应用版本策略管理。

## Session 与权限

`Session` 是双端统一的连接抽象，不区分 ClientSession / ServerSession。它提供双向请求处理、生命周期和实例级 `ConcurrentDictionary<string, object> Blackboard`。

RPC Runtime 不实现登录、鉴权、权限下发或权限校验。应用可以把用户上下文、鉴权委托或权限数据放入 `Session.Blackboard`，并在自己的 Controller 中判断。Blackboard 容器并发安全，但其中对象的线程安全由应用负责。

## 本地 Controller 注册

每个包含 `[RpcController]` 的程序集生成 `GeneratedRpcControllerCatalog`。通信两端在创建 Session 前创建注册表并注册本端 Controller：

```csharp
RpcControllerRegistry registry = GeneratedRpcControllerCatalog.CreateRegistry(
    type => type == typeof(SessionController) ? new SessionController() : null);
```

Controller 工厂在每次请求时调用，应返回新实例或请求作用域实例，不要返回共享的可变单例。多个 Session 可以共享已注册并 Seal 的只读注册表；Controller 会在每次调用前获得当前 Session。

## 构建 Source Generator

```powershell
.\Build-CodeGen.ps1
```

输出部署到 `Runtime/Analyzers/Rpc.Executor.CodeGen.dll`，Unity Analyzer Label 为 `RoslynAnalyzer`。修改后重启 Unity。

## Samples

`Samples~/Session` 演示调用方契约、Session API 注入、本地 Controller、DTO 自动生成和 Blackboard。生产项目建议将 Caller 与 Receiver 放在不同程序集，确保被调用方无需引用调用方接口。
