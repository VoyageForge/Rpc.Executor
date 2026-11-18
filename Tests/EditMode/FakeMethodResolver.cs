using System;
using System.Collections.Generic;
using VoyageForge.Rpc.Executor.Abstractions;

namespace VoyageForge.Rpc.Executor.Tests
{
    /// <summary>测试用方法解析器：按“服务名+方法名”查表返回预置描述符，不依赖源生成器。</summary>
    public sealed class FakeMethodResolver : IMethodResolver
    {
        private readonly Dictionary<string, MethodDescriptor> _methods =
            new Dictionary<string, MethodDescriptor>(StringComparer.Ordinal);

        /// <summary>注册一个方法描述符。</summary>
        public void Add(MethodDescriptor descriptor)
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            _methods[descriptor.ServiceName + "." + descriptor.MethodName] = descriptor;
        }

        /// <inheritdoc />
        public bool TryResolve(string serviceName, string methodName, object[] args, out MethodDescriptor descriptor)
        {
            descriptor = null;
            if (serviceName == null || methodName == null) return false;
            return _methods.TryGetValue(serviceName + "." + methodName, out descriptor);
        }

        /// <inheritdoc />
        public bool TryResolve(ulong methodId, object[] args, out MethodDescriptor descriptor)
        {
            descriptor = null;
            if (methodId == 0) return false;
            foreach (MethodDescriptor candidate in _methods.Values)
            {
                if (candidate.MethodId == methodId)
                {
                    descriptor = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}