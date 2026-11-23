# Controller RPC 0.3.0

本文描述 `TcpSession<TControllerMap>` 的新双向 Controller RPC 模型。它与旧版 `[RpcService]` / `ServicesMap` 执行器分离。

## 契约、调用方与被调用方

共享契约程序集声明接口：

```csharp
[Controller("User")]
public interface IUserController
{
    Task<UserInfo> GetUser(GetUserRequest request, CancellationToken ct);
}
```

调用方声明代理集合：

```csharp
[ControllerMap(typeof(IUserController))]
public partial class GameControllerMap : ControllerMap { }
```

被调用方声明业务实现：

```csharp
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

`ControllerMap` 只存在于调用方；具体 `UserController` 只存在于被调用方。双方通过共享契约接口达成一致。代理属性名默认由接口名去掉前缀 `I` 生成；`[Controller]` 的 Name 则是协议中的 ControllerName。生成器分别生成调用代理和本地 Dispatcher，不会让调用方直接引用被调用方实现。

## Catalog 与 Registry

每个包含 `[RpcController]` 的程序集生成独立静态 Catalog：

```text
VoyageForge.Rpc.Generated.<AssemblyName>.GeneratedRpcControllerCatalog
```

应用启动时聚合：

```csharp
var registry = new RpcControllerRegistry();
AccountServer.GeneratedRpcControllerCatalog.Register(registry, type => new UserController());
BattleServer.GeneratedRpcControllerCatalog.Register(registry, type => new BattleController());

var session = new TcpSession<GameControllerMap>(tcpClient, registry);
```

以上 `AccountServer` / `BattleServer` 是对应程序集名称经非标识符字符替换后的命名空间段。

`RpcControllerRegistry` 是应用级运行时注册表，多个 Session 共享。Session 构造时会 Seal Registry，因此所有 Catalog 必须先注册。重复 MethodId 注册会抛异常，不允许静默覆盖。

`controllerFactory` 在每次方法调用时执行，必须返回新的 Controller 实例或请求作用域实例。不要返回共享单例，否则 Controller 内部的可变字段可能被并发请求互相覆盖。

## MethodId v2

使用 FNV-1a 64。规范化输入：

```text
v2 + U+001F + ControllerName + U+001F + MethodName + U+001F + 参数类型规范名
```

参数类型规范名由 Roslyn `SymbolDisplayFormat.FullyQualifiedFormat` 生成，按声明顺序排列，以 U+001E 分隔。参数名和返回类型不参与；`CancellationToken` 不参与。Controller Name、Method Name、参数类型或顺序变化都会改变 MethodId，应视为协议变更。

网络请求只传：

- `RequestId`：响应关联；
- `MethodId`：定位远端方法；
- `Payload`：MemoryPack 业务数据。

Controller Name、Method Name 和签名只保存在本地 Catalog/Descriptor 中用于诊断，不进入网络请求。

## MemoryPack Payload

- 0 个业务参数：空 Payload；
- 1 个业务参数：直接序列化参数的静态类型；
- 2–7 个业务参数：按声明顺序序列化 ValueTuple；
- `Task<T>`：返回值直接 MemoryPack 序列化；
- `Task`：空响应 Payload。

单参数 `null` 仍按其静态类型序列化，不与无参数调用混淆。业务 DTO 必须由用户源码声明为 `[MemoryPackable] partial`。RPC Generator 不会 AddSource MemoryPack DTO 并期待 MemoryPack Generator 再处理它；两个 Generator 都只能可靠处理编译输入中实际存在的类型。

## 当前约束

- Controller 接口必须带 `[Controller]`；
- ControllerMap 必须是非泛型 `partial class` 并继承 `ControllerMap`；
- 服务端 Controller 必须是非抽象、非泛型 `partial class`，继承 `RpcControllerBase`，并使用 `[RpcController(typeof(IContract))]`；
- 契约当前只支持 `Task` / `Task<T>`，最多 7 个业务参数；
- `CancellationToken` 最多一个且必须最后；
- 当前契约只允许方法，不支持属性和事件。

## 双向调用

连接双方都持有自己的 Session 和本端 Registry。每一端将本端可执行的 Controller Catalog 注册进 Registry；对端通过自己的 ControllerMap 调用远端契约。读循环不会等待业务 Dispatcher 完成后才继续读取下一帧，避免 Controller 在处理请求时反向调用对端而阻塞本连接的响应读取。
