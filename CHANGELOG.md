# Changelog

## 0.2.0

- 版本统一为 0.2.0，依赖 `com.voyageforge.rpc-transport` 0.2.0。
- 命名空间 `Rpc.Executor` → **`VoyageForge.Rpc.Executor`**（与程序集名一致）。
- 新增 `IdempotencyCache`（默认 5 分钟、只缓存成功响应、RequestId 0 不参与）。
- 新增 `MessageId`/`RpcMessageAttribute`/`RpcMessageTypeRegistry` 与 CodeGen `RpcMessageTypeIdGenerator`。
- 生成器：`RpcGenerator`（字符串协议）、`RpcHandlerGenerator`（强类型目录）、`RpcMessageTypeIdGenerator`（MessageId）。
- 示例：`Tcp`、`Calculator`、`Handlers` 增加场景 + UI 按钮。
- 测试：EditMode 覆盖服务注册/参数转换/MethodId/执行器/幂等/强类型处理器。

## 0.0.17

- 幂等缓存、MessageId 生成器、EditMode 测试、文档。