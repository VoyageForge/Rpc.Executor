using System;
using System.Collections.Generic;
using NUnit.Framework;
using VoyageForge.Rpc.Executor;

namespace VoyageForge.Rpc.Executor.Tests
{
    /// <summary>FNV-1a 64 位 MethodId 测试：验证稳定性、敏感性和参数校验。</summary>
    public sealed class Fnv1A64MethodIdProviderTests
    {
        private Fnv1A64MethodIdProvider _provider;

        [SetUp]
        public void SetUp()
        {
            _provider = new Fnv1A64MethodIdProvider();
        }

        [Test]
        public void 相同输入得到相同ID()
        {
            string[] args = { "System.Int32", "System.String" };

            ulong first = _provider.GetMethodId("tests.echo", "Add", args);
            ulong second = _provider.GetMethodId("tests.echo", "Add", args);

            Assert.AreEqual(first, second);
        }

        [Test]
        public void 服务名不同ID不同()
        {
            string[] args = { "System.Int32" };

            Assert.AreNotEqual(
                _provider.GetMethodId("service.a", "Add", args),
                _provider.GetMethodId("service.b", "Add", args));
        }

        [Test]
        public void 方法名不同ID不同()
        {
            string[] args = { "System.Int32" };

            Assert.AreNotEqual(
                _provider.GetMethodId("service.a", "Add", args),
                _provider.GetMethodId("service.a", "Subtract", args));
        }

        [Test]
        public void 参数类型顺序不同ID不同()
        {
            Assert.AreNotEqual(
                _provider.GetMethodId("service.a", "Add", new[] { "System.Int32", "System.String" }),
                _provider.GetMethodId("service.a", "Add", new[] { "System.String", "System.Int32" }));
        }

        [Test]
        public void 空服务名或方法名抛异常()
        {
            Assert.Throws<ArgumentException>(() => _provider.GetMethodId(null, "Add", Array.Empty<string>()));
            Assert.Throws<ArgumentException>(() => _provider.GetMethodId(" ", "Add", Array.Empty<string>()));
            Assert.Throws<ArgumentException>(() => _provider.GetMethodId("service.a", null, Array.Empty<string>()));
            Assert.Throws<ArgumentException>(() => _provider.GetMethodId("service.a", " ", Array.Empty<string>()));
        }

        [Test]
        public void 无参数方法也能计算ID()
        {
            Assert.AreNotEqual(0UL, _provider.GetMethodId("service.a", "Ping", Array.Empty<string>()));
        }
    }
}
