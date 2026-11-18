using System;
using System.Threading;
using NUnit.Framework;
using VoyageForge.Rpc.Executor;

namespace VoyageForge.Rpc.Executor.Tests
{
    /// <summary>幂等缓存测试：TTL、成功缓存、RequestId 0 与容量行为。</summary>
    public sealed class IdempotencyCacheTests
    {
        [Test]
        public void 成功响应可命中()
        {
            IdempotencyCache cache = new IdempotencyCache();
            cache.Set(1, new VoyageForge.Rpc.Transport.RpcResponse
            {
                Success = true,
                Result = "cached"
            });

            Assert.IsTrue(cache.TryGet(1, out VoyageForge.Rpc.Transport.RpcResponse found));
            Assert.AreEqual("cached", found.Result);
        }

        [Test]
        public void 失败响应不缓存()
        {
            IdempotencyCache cache = new IdempotencyCache();
            cache.Set(2, VoyageForge.Rpc.Transport.RpcResponse.Fail("ERROR", "failed"));

            Assert.IsFalse(cache.TryGet(2, out _));
        }

        [Test]
        public void RequestId为0忽略()
        {
            IdempotencyCache cache = new IdempotencyCache();
            cache.Set(0, new VoyageForge.Rpc.Transport.RpcResponse { Success = true });

            Assert.IsFalse(cache.TryGet(0, out _));
        }

        [Test]
        public void 过期后不再命中()
        {
            IdempotencyCache cache = new IdempotencyCache(TimeSpan.FromMilliseconds(50));
            cache.Set(3, new VoyageForge.Rpc.Transport.RpcResponse { Success = true });
            Thread.Sleep(120);

            Assert.IsFalse(cache.TryGet(3, out _));
        }

        [Test]
        public void 容量满时淘汰旧项()
        {
            IdempotencyCache cache = new IdempotencyCache(TimeSpan.FromMinutes(5), 2);
            cache.Set(1, new VoyageForge.Rpc.Transport.RpcResponse { Success = true });
            cache.Set(2, new VoyageForge.Rpc.Transport.RpcResponse { Success = true });
            cache.Set(3, new VoyageForge.Rpc.Transport.RpcResponse { Success = true });

            Assert.LessOrEqual(cache.Count, 2);
        }

        [Test]
        public void 非正TTL或容量抛异常()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new IdempotencyCache(TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new IdempotencyCache(null, 0));
        }
    }
}
