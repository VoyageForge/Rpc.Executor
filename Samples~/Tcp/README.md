# TCP RPC 客户端与服务端示例

本示例演示如何把独立 `Rpc.Transport` Package 的 TCP 实现接入 `Rpc.Executor`，而不是在 TCP 代码里直接调用业务方法。

## 依赖

- `com.voyageforge.rpc-transport`：由 Executor 的 UPM 依赖自动带入，也可单独安装。
- `com.voyageforge.rpc-executor`：提供服务注册和业务调用内核。
- `com.unity.nuget.newtonsoft-json`：由 Executor manifest 声明，Sample 用它实现 `IRpcSerializer`。

RPC Runtime 本身不引用 Newtonsoft。示例把 `NewtonsoftRpcSerializer` 放在 Samples 中，说明序列化实现是可替换的。

## 运行步骤

1. 导入 `TCP RPC 客户端与服务端示例`。本 Sample 自带 `tcp.calc` 契约和实现，不依赖另一个 Calculator Sample 程序集。
2. 在任意 MonoBehaviour 或测试入口中调用 `await TcpRpcSample.RunLoopbackAsync(CancellationToken.None)`。
3. Console 输出 `TCP RPC: 3 + 4 = 7`。

导入 Sample 后，可以在 Unity Test Runner 中运行 `VoyageForge.Rpc.Executor.TcpSamples.PlayMode.Tests`。它使用真实 Loopback TCP 和 `NewtonsoftRpcSerializer`，并发往返较大体积的中英混合/Emoji 字符串，验证序列化结果一致，并记录字节吞吐量。测试不引用 Source Generator 输出的类型。

Sample 使用手写静态分发器，因此首次导入时不依赖 CodeGen DLL。你自己的 RPC 契约仍可使用 Source Generator 自动生成静态分发代码。

## 阅读顺序

1. `NewtonsoftRpcSerializer.cs`：看 RpcRequest/RpcResponse 如何直接流式写入 UTF-8，避免先生成完整 JSON 字符串再编码。
2. `Rpc.Transport/Runtime/Scripts/Transports/Tcp/TcpFrameProtocol.cs`：看 TCP 字节流如何通过 4 字节长度头恢复消息边界。
3. `Rpc.Transport/Runtime/Scripts/Transports/Tcp/TcpRpcServerTransport.cs`：看 Accept、读请求、触发 OnRequestAsync、写响应。
4. `Rpc.Transport/Runtime/Scripts/Transports/Tcp/TcpRpcClientTransport.cs`：看客户端如何连接、写请求、读响应。
5. `TcpRpcSample.cs`：看如何将 `server.OnRequestAsync` 接到 `executor.ExecuteAsync`。

## 当前 TCP 参考实现的边界

- 每条连接只承载一个请求和一个响应，完成后关闭；不是高吞吐连接池。
- 服务端可并发接受多个客户端连接。
- 单帧默认最大 1 MiB，可通过构造参数调整。
- TCP 不保留 Write 的消息边界，所以必须使用长度前缀分帧。
- 示例不提供 TLS、身份认证、连接池或自动重试；生产环境应根据威胁模型补齐。
- Unity WebGL 不支持原生 TCP Socket；WebGL 应改用 WebSocket/HTTP 传输适配器。

## 高性能字符串处理

`NewtonsoftRpcSerializer` 复用严格 UTF-8 编码器；序列化通过 `JsonTextWriter -> StreamWriter -> MemoryStream` 流式输出，不创建完整 JSON 中间字符串。反序列化使用 `UTF8Encoding.GetString(ReadOnlySpan<byte>)`，不先调用 `ToArray()` 复制输入帧。每次调用使用独立 `JsonSerializer`，避免多个客户端并发时共享可变序列化器状态。PlayMode 性能用例会用多个 TCP 客户端并发往返大字符串，并打印实际耗时和 MiB/s；测试不使用固定性能阈值，避免不同硬件环境下误报。
