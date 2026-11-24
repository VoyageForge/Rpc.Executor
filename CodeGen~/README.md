# Rpc.Executor.CodeGen

独立 `netstandard2.0` Roslyn Source Generator 项目，面向 Unity 2022.3 / C# 9。当前只保留统一 Session RPC 生成路径。

## ControllerRpcGenerator

- 扫描 `[RpcSession] partial class XxxSession : Session`（必须直接继承 Session，且不得声明构造函数或 Create 成员）。
- 读取其 `[RpcApi(typeof(IContract))]`，生成对应 API 属性和内部 Proxy。
- 生成私有构造函数和 `Create()` 工厂，外部不能直接 `new` 该 Session。
- 扫描 `[RpcController("Name")] partial class`，只处理显式标记 `[RpcMethod]` 的实例方法。
- 为本地 Controller 生成 Dispatcher 和程序集级 `GeneratedRpcControllerCatalog`。
- DTO 由本生成器构造临时 Compilation，再复用 MemoryPack Generator 的 TypeMeta / Emit 逻辑生成序列化实现。

## 协议要点

- API 契约与本地 Controller 可以分属不同程序集，不要求共享接口引用。
- 两端必须使用相同的 Controller 名称、协议方法名、参数类型及参数顺序，并保持返回类型一致。
- 参数名不属于协议；DTO 字段固定为 `Arg0`、`Arg1` 等位置名称。
- DTO 类型名包含参数签名哈希，支持同名方法的不同参数签名，且 DTO 为 `internal`。
- `CancellationToken` 不参与 DTO 和 MethodId；Session 使用独立 `RpcCancel` 帧把本地取消通知传递到对端。
- 生成器不生成鉴权代码。

## 构建

在 Package 根目录执行：

```powershell
.\Build-CodeGen.ps1
```

或在本目录执行：

```powershell
dotnet build .\Rpc.Executor.CodeGen.sln -c Release
```

构建脚本将 DLL 部署到 `Runtime/Analyzers` 并生成 Unity Analyzer 元数据。当前源码包不保证附带可直接使用的最新 DLL，修改生成器后必须重新构建并重启 Unity。
