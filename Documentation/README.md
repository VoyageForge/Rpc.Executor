# RPC Executor 文档

- `源码阅读指南.md`：从 RpcRequest、服务注册、参数绑定、执行管线到 Source Generator 的初学者阅读路线；通讯接口和 TCP 实现请同时阅读 Rpc.Transport Package 文档。
- `通讯架构.md`：说明 Rpc.Executor 与独立 Rpc.Transport 的依赖方向、请求流转与 Sample 回退实现。
- TCP 通讯代码已移至 `Rpc.Transport/Runtime/Scripts/Transports/Tcp`；TCP + RpcExecutor 的组装示例仍位于 `Samples~/Tcp`。
