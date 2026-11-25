# Changelog

## 0.4.2

- 同步 Rpc.Transport 根命名空间调整，生成代码现在直接引用 `VoyageForge.Rpc.Transport`。
- 更新 Session 继承识别、生成构造函数、Create 工厂、代理与 Catalog 的全部类型引用。

## 0.4.1

- 将传输关联字段 `RequestId` 更名为 `CallId`，明确它只用于请求/响应关联与取消。
- 移除接收端对重复 CallId 的拒绝逻辑；运行时不缓存调用结果，也不执行请求去重。
- 修正 MemoryPackSerializer 全限定名，并修正 asmdef 对 MemoryPack.Core.dll 的引用配置。

## 0.4.0

- 移除 ControllerMap 与泛型 TcpSession，统一为可继承的 Session。
- 新增 RpcSession / RpcApi，生成 API Proxy、私有构造函数与 Create 工厂。
- 被调用方 Controller 不再依赖调用方契约接口，改为 RpcController + RpcMethod。
- 为每个 Session 增加独立 Blackboard。
- 移除旧 RpcService / ServicesMap、Handler 执行通道和内置授权模型。
- DTO 改为生成器内部类型，按参数声明位置映射 Arg0、Arg1 等字段。
- 代理与 Dispatcher 的 DTO 预检前置，避免生成半成品 Catalog。
