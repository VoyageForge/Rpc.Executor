# Rpc.Executor.CodeGen

独立 `netstandard2.0` Roslyn Source Generator 项目。Unity 不编译 `CodeGen~` 中的源码，而是加载构建后部署到 `Runtime/Analyzers/Rpc.Executor.CodeGen.dll` 的 Analyzer。

## 生成器

| 生成器 | 用途 |
|---|---|
| `ControllerRpcGenerator` | 新双向 Controller RPC：生成 ControllerMap 代理、Controller Dispatcher 和每程序集 Catalog |
| `LegacyRpcGenerator`（源码文件 `RpcGenerator.cs`） | 旧 `[RpcService]` / `ServicesMap` 迁移兼容路径 |
| `RpcHandlerGenerator` | `IRequestHandler` / `IAsyncRequestHandler` 强类型处理器目录 |
| `RpcMessageTypeIdGenerator` | `[RpcMessage]` 消息类型编号 |

## 构建与部署

在 Package 根目录执行：

```powershell
.\Build-CodeGen.ps1
```

或在本目录执行：

```powershell
dotnet build .\Rpc.Executor.CodeGen.sln -c Release
```

构建脚本将 DLL 和 `RoslynAnalyzer` Asset Label 元数据部署到 `Runtime/Analyzers`。本次源码包不携带旧版预编译 DLL，首次导入 Unity 前必须先构建。更新 Analyzer 后重启 Unity。

## 约束

- 目标框架 `netstandard2.0`，语言版本 C# 9；
- 使用 `ISourceGenerator + ISyntaxReceiver`；
- Generator 只处理当前 Compilation 输入中真实存在的源码和引用符号；
- `ControllerRpcGenerator` 不会生成 MemoryPack DTO 再期待 MemoryPack Generator 处理，用户 DTO 必须在用户源码中声明为 `[MemoryPackable] partial`。
