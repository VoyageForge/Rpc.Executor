using System;

namespace VoyageForge.Rpc.Executor
{
    /// <summary>标记服务端 Controller，并指定它实现的共享 RPC 契约接口。</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class RpcControllerAttribute : Attribute
    {
        public Type ContractType { get; }
        public RpcControllerAttribute(Type contractType)
        {
            ContractType = contractType ?? throw new ArgumentNullException(nameof(contractType));
        }
    }
}
