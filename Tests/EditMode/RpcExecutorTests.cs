using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VoyageForge.Rpc.Executor;
using VoyageForge.Rpc.Executor.Abstractions;
using VoyageForge.Rpc.Transport;

namespace VoyageForge.Rpc.Executor.Tests
{
    /// <summary>字符串协议执行器测试：覆盖路由、参数绑定、取消、异常和拦截器。</summary>
    public sealed class RpcExecutorTests
    {
        private sealed class EchoService
        {
        }

        private static RpcExecutor CreateExecutor(out EchoService service, out FakeInvoker invoker,
            IEnumerable<IRpcInterceptor> interceptors = null)
        {
            ServiceRegistry registry = new ServiceRegistry();
            service = new EchoService();
            registry.Register(service, "tests.echo");

            FakeMethodResolver resolver = new FakeMethodResolver();
            resolver.Add(new MethodDescriptor
            {
                ServiceName = "tests.echo",
                MethodName = "Add",
                ParameterTypes = new[] { typeof(int), typeof(int) },
                Slot = 1,
                ResultType = typeof(int)
            });
            resolver.Add(new MethodDescriptor
            {
                ServiceName = "tests.echo",
                MethodName = "AddWithToken",
                ParameterTypes = new[] { typeof(int), typeof(CancellationToken) },
                Slot = 2,
                ResultType = typeof(int)
            });

            FakeInvoker localInvoker = null;
            localInvoker = new FakeInvoker((method, instance, payload, ct) =>
            {
                if (method.MethodName == "Add")
                {
                    (int a, int b) args = RpcPayload.Unpack<(int, int)>(payload);
                    return Task.FromResult(RpcPayload.Pack(args.a + args.b));
                }

                if (method.MethodName == "AddWithToken")
                {
                    return Task.FromResult(RpcPayload.Pack(RpcPayload.Unpack<int>(payload)));
                }

                return Task.FromResult(Array.Empty<byte>());
            });
            invoker = localInvoker;

            return new RpcExecutor(registry, resolver, invoker, new JsonArgumentConverter(), interceptors);
        }

        private static RpcRequest CreateRequest(string methodName, params object[] parameters)
        {
            return new RpcRequest
            {
                ServiceName = "tests.echo",
                MethodName = methodName,
                Payload = methodName == "Add"
                    ? RpcPayload.Pack(((int)parameters[0], (int)parameters[1]))
                    : RpcPayload.Pack((int)parameters[0]),
            };
        }

        [Test]
        public void 成功调用并转换参数()
        {
            RpcExecutor executor = CreateExecutor(out _, out _);

            RpcResponse response = executor.ExecuteAsync(
                CreateRequest("Add", 1, 2)).GetAwaiter().GetResult();

            Assert.IsTrue(response.Success);
            Assert.AreEqual(3, RpcPayload.Unpack<int>(response.Payload));
        }

        [Test]
        public void 服务不存在返回结构化错误()
        {
            RpcExecutor executor = CreateExecutor(out _, out _);

            RpcResponse response = executor.ExecuteAsync(new RpcRequest
            {
                ServiceName = "tests.missing",
                MethodName = "Add",
                Payload = RpcPayload.Pack((1, 2))
            }).GetAwaiter().GetResult();

            Assert.IsFalse(response.Success);
            Assert.AreEqual("SERVICE_NOT_FOUND", response.Error.Code);
        }

        [Test]
        public void 方法不存在返回结构化错误()
        {
            RpcExecutor executor = CreateExecutor(out _, out _);

            RpcResponse response = executor.ExecuteAsync(
                CreateRequest("Missing", 1, 2)).GetAwaiter().GetResult();

            Assert.IsFalse(response.Success);
            Assert.AreEqual("METHOD_NOT_FOUND", response.Error.Code);
        }

        [Test]
        public void CancellationToken注入后不占请求参数()
        {
            RpcExecutor executor = CreateExecutor(out _, out _);

            RpcResponse response = executor.ExecuteAsync(
                CreateRequest("AddWithToken", 5)).GetAwaiter().GetResult();

            Assert.IsTrue(response.Success);
            Assert.AreEqual(5, RpcPayload.Unpack<int>(response.Payload));
        }

        [Test]
        public void 已取消的请求返回CANCELLED()
        {
            RpcExecutor executor = CreateExecutor(out _, out _);
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                cts.Cancel();

                RpcResponse response = executor.ExecuteAsync(
                    CreateRequest("Add", 1, 2), cts.Token).GetAwaiter().GetResult();

                Assert.IsFalse(response.Success);
                Assert.AreEqual("CANCELLED", response.Error.Code);
            }
        }

        [Test]
        public void 拦截器按注册顺序包裹并短路()
        {
            List<string> calls = new List<string>();
            RpcExecutor executor = CreateExecutor(out _, out _, new IRpcInterceptor[]
            {
                new DelegateInterceptor("outer", calls),
                new DelegateInterceptor("inner", calls)
            });

            RpcResponse response = executor.ExecuteAsync(
                CreateRequest("Add", 1, 2)).GetAwaiter().GetResult();

            Assert.IsTrue(response.Success);
            CollectionAssert.AreEqual(new[] { "outer:before", "inner:before", "inner:after", "outer:after" }, calls);
        }

        [Test]
        public void 拦截器可以短路请求()
        {
            RpcExecutor executor = CreateExecutor(out _, out _, new IRpcInterceptor[]
            {
                new ShortCircuitInterceptor()
            });

            RpcResponse response = executor.ExecuteAsync(
                CreateRequest("Add", 1, 2)).GetAwaiter().GetResult();

            Assert.IsFalse(response.Success);
            Assert.AreEqual("BLOCKED", response.Error.Code);
        }

        [Test]
        public void 空请求抛异常()
        {
            RpcExecutor executor = CreateExecutor(out _, out _);

            Assert.Throws<ArgumentNullException>(() => executor.ExecuteAsync(null).GetAwaiter().GetResult());
        }
    }
}
