# VoyageForge RPC Executor

Unity 2022.3 RPC 源代码生成与本地 Controller 执行包：`com.voyageforge.rpc-executor`。

## 本地执行核心

`VoyageForge.Rpc.Executor` 运行时程序集可独立使用。业务 Controller 继承 ControllerBase；注册工厂后，`executor.Local<MyController>()` 返回工厂创建的实例。RPC 相关基类和特性位于 VoyageForge.Rpc.Transport。下述 Session 示例需同时安装 Transport。

例如，Console 在初始化时注册本地工厂，命令执行时取用 Controller。

```csharp
var executor = new LocalExecutor();
executor.Register(() => new MyController());
MyController controller = executor.Local<MyController>();
```

## 核心模型

用户只编写两类代码：

1. 调用方声明 RPC 契约接口，并在自己的 `Session` 子类上选择要调用的远端 API。
2. 被调用方编写本地 Controller；它不需要引用调用方的接口。

```csharp
// 调用方：只声明协议和 Session 所拥有的 API。
[RpcContract("Session")]
public partial interface ISessionApi
{
    Task<string> LoginAsync(string userId, string token, CancellationToken ct);
}

[RpcApi(typeof(ISessionApi))]
[RpcControllerBinding(typeof(SessionController))]
public partial class GameSession : Session { }

// 被调用方：独立声明本地执行方法，不实现 ISessionApi。
[RpcController("Session")]
public partial class SessionController : RpcControllerBase
{
    [RpcMethod]
    public Task<string> LoginAsync(string account, string credential, CancellationToken ct)
    {
        // 当前请求对应的 Session 可通过 Session 属性访问。
        return Task.FromResult("Connected: " + account);
    }
}
```

Session 的 partial 声明直接提供创建入口：

```csharp
GameSession session = GameSession.Create(tcpClient);
string result = await session.Remote<ISessionApi>().LoginAsync(userId, token, ct);
```

用户不需要 `new Session`、创建 Proxy、创建 DTO 或手动做参数打包。不同 Session 类型可以声明不同的 `[RpcApi]`，每个实例拥有自己的代理和 Blackboard。业务程序集需要能访问 `MemoryPack.Core.dll`，生成代码会直接调用 `global::MemoryPack.MemoryPackSerializer`。MemoryPack 运行时必须通过 NuGetForUnity 安装 NuGet 包 `MemoryPack`（本包按 1.21.4 对齐）；仅安装 `MemoryPack.Unity` 扩展不足以提供核心序列化 API。注意 `MemoryPack.Core.dll` 是预编译 DLL：默认 asmdef（`overrideReferences: false`）会自动引用预编译程序集，不要把 `MemoryPack.Core` 填入 `references`；若启用了 `overrideReferences: true`，请将 `MemoryPack.Core.dll` 添加到 `precompiledReferences`。

## 双端协议约定

- API 接口使用 `[RpcContract("名称")]` 声明协议 Controller 名称。
- 本地 Controller 使用 `[RpcController("名称")]` 声明相同名称。
- 接口方法与本地方法使用相同的方法协议名；本地方法通过 `[RpcMethod]` 显式暴露，可用 `[RpcMethod("协议方法名")]` 自定义名称。
- 参数类型与声明顺序必须一致，返回类型也必须一致；参数变量名称可以不同。
- 只支持 `Task` / `Task<T>`，最多 7 个业务参数；`CancellationToken` 可选且必须是最后一个参数。
- `CancellationToken` 不进入 DTO 和 MethodId；本地取消会通过 `RpcCancel` 通知对端，远端 Controller 收到取消后可通过自己的 `CancellationToken` 协作终止执行。
- 调用侧 DTO 随接口定义，接收侧 DTO 嵌套在 Controller 中，属性顺序与业务参数一致。
- 0 个业务参数使用空 Payload；有参数时在所属类型中生成 DTO，并在同一轮编译中调用 MemoryPack 生成序列化实现。

MethodId 使用 FNV-1a 64，协议 Key 包含版本、Controller 名称、方法名和按声明顺序排列的业务参数类型规范名。不要仅通过更改方法参数顺序来做无兼容升级；协议变更应按应用版本策略管理。

## Session 与权限

`Session` 是双端统一的连接抽象，不区分 ClientSession / ServerSession。它提供双向请求处理、生命周期和实例级 `ConcurrentDictionary<string, object> Blackboard`。

RPC Runtime 不实现登录、鉴权、权限下发或权限校验。应用可以把用户上下文、鉴权委托或权限数据放入 `Session.Blackboard`，并在自己的 Controller 中判断。Blackboard 容器并发安全，但其中对象的线程安全由应用负责。

## 本地 Controller 注册

Session 上的 `[RpcControllerBinding]` 决定入站路由。Create 为每种 Controller 传入强类型工厂。默认使用可访问的无参构造函数，也可以传入自己的工厂。控制器由工厂按调用解析，入站分发与 session.Local<T>() 共用注册信息。

修改协议、Controller 或绑定后，保存即可在当前编译中生成对应成员。详见 [RPC 编译期生成](CodeGen~/README.md)。

## 构建 Source Generator

```powershell
.\Build-CodeGen.ps1
```

输出部署到 `Runtime/Analyzers/Rpc.Executor.CodeGen.dll`，Unity Analyzer Label 为 `RoslynAnalyzer`。Unity 刷新 Analyzer 后会重新编译受影响的程序集。

## Samples

`Samples~/Session` 演示调用方契约、Session API 注入、本地 Controller、DTO 自动生成和 Blackboard。生产项目建议将 Caller 与 Receiver 放在不同程序集，确保被调用方无需引用调用方接口。
