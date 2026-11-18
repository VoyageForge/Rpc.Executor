using System;
using NUnit.Framework;
using VoyageForge.Rpc.Executor;
using VoyageForge.Rpc.Executor.Abstractions;

namespace VoyageForge.Rpc.Executor.Tests
{
    /// <summary>服务注册表测试：覆盖名称解析、覆盖策略和无效名称。</summary>
    public sealed class ServiceRegistryTests
    {
        private sealed class TestService
        {
        }

        [Test]
        public void 显式名称注册后可按名称查找()
        {
            ServiceRegistry registry = new ServiceRegistry();
            TestService instance = new TestService();

            registry.Register(instance, "tests.explicit");

            Assert.IsTrue(registry.TryGet("tests.explicit", out object found));
            Assert.AreSame(instance, found);
        }

        [Test]
        public void 未指定名称时回退到类型全名()
        {
            ServiceRegistry registry = new ServiceRegistry();
            TestService instance = new TestService();

            registry.Register(instance);

            Assert.IsTrue(registry.TryGet(typeof(TestService).FullName, out object found));
            Assert.AreSame(instance, found);
        }

        [Test]
        public void 空名称查找返回false()
        {
            ServiceRegistry registry = new ServiceRegistry();

            Assert.IsFalse(registry.TryGet(null, out _));
            Assert.IsFalse(registry.TryGet("", out _));
            Assert.IsFalse(registry.TryGet("  ", out _));
        }

        [Test]
        public void 重复注册默认覆盖旧实例()
        {
            ServiceRegistry registry = new ServiceRegistry();
            TestService first = new TestService();
            TestService second = new TestService();
            registry.Register(first, "tests.override");

            registry.Register(second, "tests.override");

            Assert.IsTrue(registry.TryGet("tests.override", out object found));
            Assert.AreSame(second, found);
        }

        [Test]
        public void 重复注册且ThrowOnDuplicate时抛异常()
        {
            ServiceRegistry registry = new ServiceRegistry();
            registry.Register(new TestService(), "tests.duplicate");

            Assert.Throws<InvalidOperationException>(() =>
                registry.Register(new TestService(), "tests.duplicate",
                    new RegisterOptions { ThrowOnDuplicate = true }));
        }

        [Test]
        public void 注册空实例抛异常()
        {
            ServiceRegistry registry = new ServiceRegistry();

            Assert.Throws<ArgumentNullException>(() => registry.Register<TestService>(null, "tests.null"));
        }
    }
}
