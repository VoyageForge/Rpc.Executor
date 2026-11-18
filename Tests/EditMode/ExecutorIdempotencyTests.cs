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
    /// <summary>执行器幂等行为测试：同一 RequestId 只执行一次，失败允许重试。</summary>
    public sealed class ExecutorIdempotencyTests
    {
        private sealed class CounterService
        {
            public int Calls;
        }

        private static RpcExecutor CreateExecutor(CounterService service, IdempotencyCache cache)
        {
            ServiceRegistry registry = new ServiceRegistry();
            registry.Register(service, "tests.counter");

            FakeMethodResolver resolver = new FakeMethodResolver();
            resolver.Add(new MethodDescriptor
            {
                ServiceName = "tests.counter",
                MethodName = "Increment",
                ParameterTypes = Type.EmptyTypes,
                Slot = 1,
                ResultType = typeof(int)
            });

            FakeInvoker invoker = new FakeInvoker((method, instance, boundArgs, ct) =>
            {
                service.Calls++;
                return Task.FromResult<object>(service.Calls);
            });

            return new RpcExecutor(registry, resolver, invoker, new JsonArgumentConverter(), null, cache);
        }

        private static RpcRequest CreateRequest(uint requestId)
        {
            return new RpcRequest
            {
                ServiceName = "tests.counter",
                MethodName = "Increment",
                RequestId = requestId
            };
        }

        [Test]
        public void 同一RequestId只执行一次()
        {
            CounterService service = new CounterService();
            RpcExecutor executor = CreateExecutor(service, new IdempotencyCache());

            RpcResponse first = executor.ExecuteAsync(CreateRequest(10)).GetAwaiter().GetResult();
            RpcResponse second = executor.ExecuteAsync(CreateRequest(10)).GetAwaiter().GetResult();

            Assert.AreEqual(1, service.Calls);
            Assert.AreEqual(first.Result, second.Result);
        }

        [Test]
        public void 不同RequestId各自执行()
        {
            CounterService service = new CounterService();
            RpcExecutor executor = CreateExecutor(service, new IdempotencyCache());

            executor.ExecuteAsync(CreateRequest(11)).GetAwaiter().GetResult();
            executor.ExecuteAsync(CreateRequest(12)).GetAwaiter().GetResult();

            Assert.AreEqual(2, service.Calls);
        }

        [Test]
        public void 失败不缓存可重试()
        {
            CounterService service = new CounterService();
            RpcExecutor executor = CreateExecutor(service, new IdempotencyCache());
            // 第一次请求参数数量不符（失败），不写缓存。
            RpcResponse failed = executor.ExecuteAsync(new RpcRequest
            {
                ServiceName = "tests.counter",
                MethodName = "Increment",
                RequestId = 13,
                Parameters = new object[] { "unexpected" }
            }).GetAwaiter().GetResult();

            RpcResponse retried = executor.ExecuteAsync(CreateRequest(13)).GetAwaiter().GetResult();

            Assert.IsFalse(failed.Success);
            Assert.IsTrue(retried.Success);
            Assert.AreEqual(1, service.Calls);
        }

        [Test]
        public void 取消的请求返回CANCELLED且不缓存()
        {
            CounterService service = new CounterService();
            RpcExecutor executor = CreateExecutor(service, new IdempotencyCache());
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                cts.Cancel();
                RpcResponse cancelled = executor.ExecuteAsync(CreateRequest(14), cts.Token)
                    .GetAwaiter().GetResult();

                Assert.IsFalse(cancelled.Success);
                Assert.AreEqual("CANCELLED", cancelled.Error.Code);
            }
        }
    }
}
