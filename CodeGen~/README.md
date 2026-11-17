# Rpc.Executor.CodeGen —— 独立 C# 解决方案

```text
CodeGen~/
├── Rpc.Executor.CodeGen.sln
├── Rpc.Executor.CodeGen/
│   ├── Rpc.Executor.CodeGen.csproj
│   └── RpcGenerator.cs
└── Templates/
    └── Rpc.Executor.CodeGen.dll.meta.template
```

## 编译方式

在 Visual Studio / Rider 中打开 `Rpc.Executor.CodeGen.sln`；也可以直接执行：

```powershell
dotnet build .\Rpc.Executor.CodeGen.sln -c Release
```

在 Package 根目录使用：

```powershell
.\Build-CodeGen.ps1
```

脚本编译整个解决方案，检查 DLL 存在后，将它复制到 `Runtime/Analyzers/Rpc.Executor.CodeGen.dll`，并把稳定的 Unity `.meta` 模板一起复制到旁边。

## 为什么源码和 DLL 要分开放

- `.sln` / `.csproj` / `.cs` 是开发源代码，供 Visual Studio、Rider 和 dotnet 使用。
- Unity 不会把 `CodeGen~` 中的源码当作游戏 Runtime 脚本编译。
- Unity 实际加载的是 `Runtime/Analyzers` 里的 DLL，并依据 DLL 的 `RoslynAnalyzer` 标签把它作为编译期 Source Generator 使用。
- DLL 是编译产物，不需要放入 Runtime asmdef 的普通程序集引用列表。

Unity 2022.3 的 Roslyn 兼容版本是 3.8，项目目标为 netstandard2.0，因此 csproj 固定这两个版本约束。该生成器目前使用 `ISourceGenerator + ISyntaxReceiver`，不是增量生成器 API。

## 验证方式

本项目的职责是独立编译 Source Generator，构建脚本只检查编译退出码与 DLL 部署结果。
Unity 功能测试通过公开 Runtime API 验证 RPC/Console 行为，不直接引用生成器或生成器产出的类型。


Analyzer DLL 是编译期 Roslyn Source Generator，不是普通 Unity Runtime 程序集；不要把它加入 `.asmdef` 的 `references` 或 `precompiledReferences`。它通过 `.meta` 中的 `RoslynAnalyzer` Asset Label 被 Unity 编译流程识别。
