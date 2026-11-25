# RPC 编译期生成

声明 RPC 的顶层类型使用 `partial`。修改业务声明并保存即可。Unity 编译对应程序集时，Roslyn 在内存中合并以下成员：

| 声明 | 同一个类型中生成的成员 |
|---|---|
| 契约接口 | 调用侧参数 DTO，例如 `IAccountApi.LoginRequest` |
| Controller | 接收侧参数 DTO、方法注册和强类型分发 |
| Session | `Create(socket)`、Controller 装配和出站代理 |

在支持 Roslyn 生成文档的 IDE 中，从生成成员转到定义可阅读对应类型的 `*.Rpc.g.cs`。这些是编译器文档，生成器不会写回项目源文件。

同一轮中的其他生成器看不到 RPC 生成的 DTO。RPC 生成器将 DTO 语法树加入内存中的编译模型，调用 MemoryPack.Generator 1.21.4 的 TypeMeta 生成 formatter，再交给当前编译。修改生成器本身时，运行包根目录的 `Build-CodeGen.ps1` 构建和部署 Analyzer。
