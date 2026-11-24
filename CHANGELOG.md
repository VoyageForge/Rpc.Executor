# Changelog

## 0.4.0

- 移除 ControllerMap 与泛型 TcpSession，统一为可继承的 Session。
- 新增 RpcSession / RpcApi，生成 API Proxy、私有构造函数与 Create 工厂。
- 被调用方 Controller 不再依赖调用方契约接口，改为 RpcController + RpcMethod。
- 为每个 Session 增加独立 Blackboard。
- 移除旧 RpcService / ServicesMap、Handler 执行通道和内置授权模型。
- DTO 改为生成器内部类型，按参数声明位置映射 Arg0、Arg1 等字段。
- 代理与 Dispatcher 的 DTO 预检前置，避免生成半成品 Catalog。
