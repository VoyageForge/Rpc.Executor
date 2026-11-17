using System;
using NUnit.Framework;
using VoyageForge.Rpc.Runtime.Handlers;

namespace VoyageForge.Rpc.Executor.Tests
{
    /// <summary>强类型执行器测试：覆盖同步/异步通道和通道不匹配错误。</summary>
    public sealed class HandlersRpcExecutorTests
    {
        [Test]
        public void Send走同步处理器()
        {
            VoyageForge.Rpc.Runtime.Handlers.RpcExecutor executor =
                new VoyageForge.Rpc.Runtime.Handlers.RpcExecutor();

            TestEchoResponse response = executor.Send<TestEchoRequest, TestEchoResponse>(
                new TestEchoRequest { Text = "sync" });

            Assert.AreEqual("sync:sync", response.Text);
        }

        [Test]
        public void SendAsync走异步处理器()
        {
            VoyageForge.Rpc.Runtime.Handlers.RpcExecutor executor =
                new VoyageForge.Rpc.Runtime.Handlers.RpcExecutor();

            TestAsyncEchoResponse response = executor.SendAsync<TestAsyncEchoRequest, TestAsyncEchoResponse>(
                new TestAsyncEchoRequest { Text = "async" }).GetAwaiter().GetResult();

            Assert.AreEqual("async:async", response.Text);
        }

        [Test]
        public void Send对只有异步处理器的请求抛异常()
        {
            VoyageForge.Rpc.Runtime.Handlers.RpcExecutor executor =
                new VoyageForge.Rpc.Runtime.Handlers.RpcExecutor();

            Assert.Throws<InvalidOperationException>(() =>
                executor.Send<TestAsyncEchoRequest, TestAsyncEchoResponse>(new TestAsyncEchoRequest()));
        }

        [Test]
        public void SendAsync对只有同步处理器的请求抛异常()
        {
            VoyageForge.Rpc.Runtime.Handlers.RpcExecutor executor =
                new VoyageForge.Rpc.Runtime.Handlers.RpcExecutor();

            Assert.Throws<InvalidOperationException>(() =>
                executor.SendAsync<TestEchoRequest, TestEchoResponse>(new TestEchoRequest())
                    .GetAwaiter().GetResult());
        }

        [Test]
        public void 未注册请求类型抛异常()
        {
            VoyageForge.Rpc.Runtime.Handlers.RpcExecutor executor =
                new VoyageForge.Rpc.Runtime.Handlers.RpcExecutor();

            Assert.Throws<InvalidOperationException>(() =>
                executor.Send<string, string>("no-handler"));
        }
    }
}
