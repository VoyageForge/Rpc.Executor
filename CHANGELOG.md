## 0.3.1

### 修复

- **`MethodDescriptor.Slot 漏赋值**：生成器构造描述符时没有写入 `Slot`，而它是普通 `int` 字段（默认 0），
  于是所有方法的 Slot 都是 0 —— `DispatchAsync` 永远执行 `case 0`，调用方法 B 实际执行方法 A，
  且编译期没有任何提示。典型症状：单参数方法返回 null（拿到的是另一个方法的 payload）。
  现在 `EmitDescriptor` 显式写入 `Slot = {slot}`。

### 移植

- Controller-RPC 的请求 DTO 改为**自动生成**（临时 Compilation + 复用 MemoryPack Emitter），
  payload 由 ValueTuple 改为 `{Controller}{Method}Request` DTO，调用端与服务端命名规则一致。
# Changelog

## 0.3.0

- 新增 ControllerRpcGenerator：为 ControllerMap 生成强类型代理，为 RpcControllerBase 派生类生成 Dispatcher。
- 每个服务程序集生成独立 GeneratedRpcControllerCatalog，应用启动时聚合注册到 RpcControllerRegistry。
- 新增 RpcControllerAttribute；服务端 Controller 必须显式绑定共享契约接口。
- 旧 RpcService / ServicesMap 与 Handler 通道暂时保留，和新 TcpSession Controller RPC 分离。

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