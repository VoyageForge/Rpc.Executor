using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VoyageForge.Rpc.Executor;
using VoyageForge.Rpc.Executor.Abstractions;
using VoyageForge.Rpc.Generated;
using VoyageForge.Rpc.Transport;

namespace VoyageForge.Rpc.Executor.Tests
{
    /// <summary>ServicesMap + 生成器（元组/DTO 规则）全链路测试。</summary>
    public sealed class ServicesMapEndToEndTests
    {
        [RpcService("tests.calc")]
        public interface ITestCalc
        {
            /// <summary>2 参数（元组）。</summary>
            RpcResponse<int> Add(int a, int b);

            /// <summary>1 参数（直接序列化）。</summary>
            RpcResponse<string> Echo(string text);
        }

        /// <summary>测试实现。</summary>
        public sealed class TestCalc : ITestCalc
        {
            public RpcResponse<int> Add(int a, int b) { return new RpcResponse<int> { Data = a + b }; }
            public RpcResponse<string> Echo(string text) { return new RpcResponse<string> { Data = "echo:" + text }; }
        }

        private static RpcExecutor CreateExecutor()
        {
            ServiceRegistry registry = new ServiceRegistry();
            registry.Register<ITestCalc>(new TestCalc(), "tests.calc");
            IGeneratedRpcDispatcher dispatcher = new RpcGeneratedDispatcher();
            return new RpcExecutor(registry, new GeneratedMethodResolver(dispatcher), new GeneratedRpcInvoker(dispatcher));
        }

        [Test]
        public void ServicesMap调用两参数方法()
        {
            ServicesMap api = new ServicesMap(CreateExecutor());

            RpcResponse<int> response = api.Add(3, 4).GetAwaiter().GetResult();

            Assert.IsTrue(response.Success,
                "失败：" + (response.Error != null ? response.Error.Code + ":" + response.Error.Message : "无错误信息"));
            Assert.AreEqual(7, response.Data);
        }

        [Test]
        public void ServicesMap调用单参数方法()
        {
            ServicesMap api = new ServicesMap(CreateExecutor());

            RpcResponse<string> response = api.Echo("hi").GetAwaiter().GetResult();

            Assert.IsTrue(response.Success,
                "失败：" + (response.Error != null ? response.Error.Code + ":" + response.Error.Message : "无错误信息"));
            Assert.AreEqual("echo:hi", response.Data);
        }
    }
}