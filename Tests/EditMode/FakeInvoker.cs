using System;
using System.Threading;
using System.Threading.Tasks;
using VoyageForge.Rpc.Executor.Abstractions;

namespace VoyageForge.Rpc.Executor.Tests
{
    /// <summary>测试用调用器：直接调用预置委托，模拟生成的分发 switch。</summary>
    public sealed class FakeInvoker : IInvoker
    {
        private readonly Func<MethodDescriptor, object, byte[], CancellationToken, Task<object>> _invoke;

        /// <summary>注入真正的调用逻辑。</summary>
        public FakeInvoker(Func<MethodDescriptor, object, byte[], CancellationToken, Task<object>> invoke)
        {
            _invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
        }

        /// <inheritdoc />
        public Task<object> InvokeAsync(MethodDescriptor method, object instance, byte[] payload,
            CancellationToken ct)
        {
            return _invoke(method, instance, payload, ct);
        }
    }
}