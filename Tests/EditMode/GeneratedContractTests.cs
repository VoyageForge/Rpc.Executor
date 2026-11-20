using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VoyageForge.Rpc.Executor;
using VoyageForge.Rpc.Executor.Abstractions;
using VoyageForge.Rpc.Transport;

namespace VoyageForge.Rpc.Executor.Tests
{
    /// <summary>生成器边界测试：验证 GeneratedMethodResolver / GeneratedRpcInvoker 的委托行为。</summary>
    public sealed class GeneratedContractTests
    {
        private sealed class FakeDispatcher : IGeneratedRpcDispatcher
        {
            public MethodDescriptor Descriptor { get; set; }

            public int DispatchedSlot { get; private set; }

            public byte[] DispatchedPayload { get; private set; }

            public bool TryResolve(string serviceName, string methodName, object[] args,
                out MethodDescriptor descriptor)
            {
                descriptor = Descriptor;
                return descriptor != null;
            }

                    /// <summary>Mirror 式：按 MethodId 命中；示例/测试不使用，返回 false。</summary>
        public bool TryResolve(ulong methodId, object[] args, out MethodDescriptor descriptor)
        {
            descriptor = null;
            return false;
        }
public Task<byte[]> DispatchAsync(int slot, object instance, byte[] payload, CancellationToken ct)
            {
                DispatchedSlot = slot;
                DispatchedPayload = payload;
                return Task.FromResult(RpcPayload.Pack("dispatched"));
            }
        }

        [Test]
        public void 解析器委托给生成分发表()
        {
            FakeDispatcher dispatcher = new FakeDispatcher
            {
                Descriptor = new MethodDescriptor { ServiceName = "tests", MethodName = "Run", Slot = 7 }
            };
            GeneratedMethodResolver resolver = new GeneratedMethodResolver(dispatcher);

            bool found = resolver.TryResolve("tests", "Run", Array.Empty<object>(), out MethodDescriptor descriptor);

            Assert.IsTrue(found);
            Assert.AreEqual(7, descriptor.Slot);
        }

        [Test]
        public void 调用器按Slot进入生成switch()
        {
            FakeDispatcher dispatcher = new FakeDispatcher();
            GeneratedRpcInvoker invoker = new GeneratedRpcInvoker(dispatcher);
            MethodDescriptor descriptor = new MethodDescriptor { ServiceName = "tests", MethodName = "Run", Slot = 9 };
            byte[] payload = { 1, 2 };

            byte[] result = invoker.InvokeAsync(descriptor, this, payload, CancellationToken.None)
                .GetAwaiter().GetResult();

            Assert.AreEqual("dispatched", RpcPayload.Unpack<string>(result));
            Assert.AreEqual(9, dispatcher.DispatchedSlot);
            Assert.AreSame(payload, dispatcher.DispatchedPayload);
        }

        [Test]
        public void 空描述符抛异常()
        {
            GeneratedRpcInvoker invoker = new GeneratedRpcInvoker(new FakeDispatcher());

            Assert.Throws<ArgumentNullException>(() =>
                invoker.InvokeAsync(null, this, Array.Empty<byte>(), CancellationToken.None));
        }
    }
}
