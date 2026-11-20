using System;
using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VoyageForge.Rpc.Transport;
using VoyageForge.Rpc.Transport.Tcp;

namespace VoyageForge.Rpc.Samples.Tcp.Tests
{
    /// <summary>
    /// 对示例中实际使用的 NewtonsoftRpcSerializer 做真实 TCP 往返测试。
    /// 测试只使用手工构造的 RpcRequest，不引用任何 Source Generator 输出的类型。
    /// </summary>
    public sealed class NewtonsoftRpcSerializerPlayModeTests
    {
        private const float TimeoutSeconds = 30f;
        private const int ClientCount = 4;
        private TcpRpcServerTransport _server;

        [TearDown]
        public void TearDown()
        {
            // 即使断言失败也关闭监听器，避免影响后续 Sample/PlayMode 测试。
            if (_server != null)
            {
                try { _server.StopAsync(CancellationToken.None).GetAwaiter().GetResult(); }
                catch { }
                _server = null;
            }
        }

        /// <summary>
        /// 多个客户端并发往返大体积 Unicode 字符串，验证序列化器无共享可变 JsonSerializer 状态，
        /// 并记录实际 UTF-8 字节吞吐量，防止未来重构引入逐字符拼接或额外整帧复制。
        /// </summary>
        [UnityTest]
        public IEnumerator NewtonsoftSerializer_ConcurrentLargeUnicodeStringsOverTcp()
        {
            CountingNewtonsoftSerializer serializer = new CountingNewtonsoftSerializer();
            TcpRpcServerTransport server = new TcpRpcServerTransport(
                IPAddress.Loopback, 0, serializer, maxFrameBytes: 4 * 1024 * 1024);
            _server = server;
            server.OnRequestAsync += (request, serverToken) =>
            {
                string text = RpcPayload.Unpack<object[]>(request.Payload).Length > 0
                    ? RpcPayload.Unpack<object[]>(request.Payload)[0] as string
                    : null;
                return Task.FromResult(RpcResponse.Ok(text));
            };
            server.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

            StringBuilder builder = new StringBuilder(512 * 1024);
            for (int index = 0; index < 8192; index++)
                builder.Append("VoyageForge | 通讯测试 | 中文字符串 | 🚀🌏 | UTF-8 | ");
            string baseText = builder.ToString();
            int payloadBytes = Encoding.UTF8.GetByteCount(baseText);

            Task<RpcResponse>[] requests = new Task<RpcResponse>[ClientCount];
            string[] expected = new string[ClientCount];
            Stopwatch stopwatch = Stopwatch.StartNew();
            for (int index = 0; index < ClientCount; index++)
            {
                expected[index] = index.ToString() + ":" + baseText;
                RpcRequest request = new RpcRequest
                {
                    ServiceName = "tests.serializer",
                    MethodName = "Echo",
                    Payload = RpcPayload.Pack( expected[index] )
                };
                TcpRpcClientTransport client = new TcpRpcClientTransport(
                    "127.0.0.1", server.LocalEndPoint.Port, serializer, maxFrameBytes: 4 * 1024 * 1024);
                requests[index] = client.SendAsync(request, CancellationToken.None);
            }

            Task allRequests = Task.WhenAll(requests);
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!allRequests.IsCompleted && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.IsTrue(allRequests.IsCompleted, "并发大字符串 TCP 往返超时。");
            stopwatch.Stop();

            if (allRequests.IsFaulted)
                throw allRequests.Exception.GetBaseException();
            for (int index = 0; index < requests.Length; index++)
            {
                Assert.IsTrue(requests[index].Result.Success);
                Assert.AreEqual(expected[index], requests[index].Result.Result,
                    "第 " + index + " 个客户端返回的 Unicode 字符串不一致。");
            }

            // 统计请求与响应两个方向的数据量。这里是观测值而非硬件相关的固定性能门槛。
            // Counting Serializer 统计真实序列化后的请求/响应 Payload 字节；每个 TCP 帧还包含 4 字节长度头。
            long serializedPayloadBytes = serializer.SerializedBytes;
            long tcpFrameHeaderBytes = ClientCount * 8L; // 每次请求/响应各有一个 4 字节应用层长度头。
            long totalFrameBytes = serializedPayloadBytes + tcpFrameHeaderBytes;
            double transferredMiB = totalFrameBytes / (1024.0 * 1024.0);
            double throughputMiBPerSecond = stopwatch.Elapsed.TotalSeconds <= 0
                ? 0
                : transferredMiB / stopwatch.Elapsed.TotalSeconds;
            TestContext.Progress.WriteLine(
                "[Newtonsoft TCP UTF-8] clients={0}, chars/client={1:N0}, UTF8 text bytes/client={2:N0}, serialized payload={3:N0} bytes, 4-byte frame headers={4:N0} bytes, total TCP framed bytes={5:N0} bytes, elapsed={6:F2} ms, aggregate round-trip={7:F2} MiB/s",
                ClientCount, baseText.Length, payloadBytes, serializedPayloadBytes, tcpFrameHeaderBytes, totalFrameBytes, stopwatch.Elapsed.TotalMilliseconds, throughputMiBPerSecond);

            server.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>包装真实 Newtonsoft 序列化器并统计每次实际输出的 Payload 字节数；计数器可安全地跨客户端/服务端并发更新。</summary>
        private sealed class CountingNewtonsoftSerializer : IRpcSerializer
        {
        public object Deserialize(byte[] data, global::System.Type type) => Deserialize<object>(data);

            private readonly NewtonsoftRpcSerializer _inner = new VoyageForge.Rpc.Transport.NewtonsoftRpcSerializer();
            private long _serializedBytes;

            public string ContentType { get { return _inner.ContentType; } }
            public long SerializedBytes { get { return Interlocked.Read(ref _serializedBytes); } }

            public byte[] Serialize<T>(T value)
            {
                byte[] payload = _inner.Serialize(value);
                if (payload != null) Interlocked.Add(ref _serializedBytes, payload.LongLength);
                return payload;
            }

            public T Deserialize<T>(ReadOnlySpan<byte> data)
            {
                return _inner.Deserialize<T>(data);
            }
        }
    }
}
