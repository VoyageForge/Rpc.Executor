# Session 双向 RPC 示例

本示例展示新的 Session 架构：

- 调用方通过 `[RpcContract]` 声明 API 接口。
- Session 通过 `[RpcApi(typeof(...))]` 选择自己拥有的远端 API。
- 编译器在 Session 的 partial 声明中生成远端代理、私有构造函数和 `Create()`。
- 被调用方通过 `[RpcController("Session")]` 与 `[RpcMethod]` 声明本地执行入口，不需要引用调用方接口。
- 双端根据 Controller 名称、方法名、业务参数类型和参数顺序计算相同的 MethodId。
- DTO 随接口和 Controller 生成，MemoryPack 序列化字段按参数声明顺序排列；参数名可以不同。CallId 仅用于调用关联；Runtime 不执行请求去重或结果缓存。
- 每个 Session 都有独立 Blackboard；示例将应用自定义的鉴权委托放入其中。

真实项目应把 `Contracts` / `Caller` 与 `Receiver` 放在不同程序集，以验证两端没有接口引用耦合。这里放在同一个 Sample 程序集中只是为了便于展示完整装配流程。
