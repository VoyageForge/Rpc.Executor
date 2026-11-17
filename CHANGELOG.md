# Changelog

## 0.0.17

- 新增 `IdempotencyCache`：默认 5 分钟 TTL、只缓存成功响应、`RequestId == 0` 不参与、容量上限；`RpcExecutor` 默认接入。
- 新增 `MessageId`/`RpcMessageAttribute`/`RpcMessageTypeRegistry` 与 CodeGen `RpcMessageTypeIdGenerator`（生成 `RpcMessageIds`/`RpcMessageTypeMap`）。
- 新增 EditMode 测试：服务注册、参数转换、MethodId、字符串协议执行器、拦截器、生成器边界、幂等缓存、强类型执行器。
- CodeGen 升级 Roslyn 4.1、`LangVersion` 9.0；`Rpc.Executor/Build-CodeGen.ps1` 中 Console 构建改为可选。
- 文档更新：`README.md`、`Documentation/架构说明.md`、`CHANGELOG.md`。

## 0.0.16

- 从旧 `VoyageForge.Rpc` 迁移为独立 `Rpc.Executor` 包（字符串协议 + 强类型处理器合并）。