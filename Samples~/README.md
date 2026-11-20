# RPC Executor Samples

三个示例：

| 示例 | 内容 |
|---|---|
| `Calculator` | 基础：契约 + 生成器分发/代理 + `ServicesMap` 调用 |
| `Tcp` | TCP 传输 + `RpcExecutor` + 生成代理 |
| `Handlers` | 强类型处理器（`IRequestHandler` / `IAsyncRequestHandler`） |

## 导入方式（推荐：软链接）

TestDepot 里有编辑器扩展 `VoyageForge → Sample Linker`：

- 勾选 = 在 `Assets/Samples` 下建立到包内 `Samples~` 的软链接（Windows 用 junction）；
- 取消 = 删除链接；
- `移除现有导入` = 删除旧的示例副本。

软链接后示例直接来自包内 `Samples~`，改文件即时生效；注意 Unity 会因软链接关闭目录监控，改完需要手动 `Assets → Refresh`。

## 逐示例说明

- `Calculator/README.md`
- `Tcp/README.md`
- `Handlers`：看 `HandlersSample.cs` 与 `EchoHandler.cs` / `EchoAsyncHandler.cs`。