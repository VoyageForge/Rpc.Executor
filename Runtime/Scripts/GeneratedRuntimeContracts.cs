using System;
using System.Threading;
using System.Threading.Tasks;
using VoyageForge.Rpc.Executor.Abstractions;

namespace VoyageForge.Rpc.Executor
{
    /// <summary>RPC Source Generator 生成的静态分发表必须实现的运行时接口。</summary>
    /// <remarks>
    /// 该接口是 Runtime 和 CodeGen 两个独立 C# 项目的边界协议：Runtime 不引用 Roslyn，CodeGen 不引用 Unity。
    /// 生成器输出普通 C# 类并实现这个接口，最终业务程序集只看见这个普通接口和实现类。
    /// </remarks>
    public interface IGeneratedRpcDispatcher
    {
        /// <summary>根据请求信息选出唯一描述符。这里只做重载选择，不执行真正的类型转换。</summary>
        bool TryResolve(string serviceName, string methodName, object[] args, out MethodDescriptor descriptor);

        /// <summary>Mirror 式：按 MethodId（FNV-1a 64）直接命中。</summary>
        bool TryResolve(ulong methodId, object[] args, out MethodDescriptor descriptor);

        /// <summary>根据 Slot 进入生成的 switch；boundArgs 已含框架注入的 CancellationToken。</summary>
        Task<object> DispatchAsync(int slot, object instance, object[] boundArgs, CancellationToken ct);
    }

    /// <summary>多个重载无法通过参数可转换性唯一确定时抛出的异常。</summary>
    public sealed class RpcMethodAmbiguousException : Exception
    {
        /// <summary>创建歧义异常，并记录对应服务与方法名。</summary>
        public RpcMethodAmbiguousException(string serviceName, string methodName)
            : base("RPC 方法重载存在歧义：" + serviceName + "." + methodName)
        {
            ServiceName = serviceName;
            MethodName = methodName;
        }

        /// <summary>发生歧义的服务名。</summary>
        public string ServiceName { get; }

        /// <summary>发生歧义的方法名。</summary>
        public string MethodName { get; }
    }

    /// <summary>把生成分发表适配到 IInvoker，避免 Runtime 依赖生成器具体类。</summary>
    public sealed class GeneratedRpcInvoker : IInvoker
    {
        private readonly IGeneratedRpcDispatcher _dispatcher;

        /// <summary>构造时注入生成器产物。推荐与 GeneratedMethodResolver 使用同一个实例。</summary>
        public GeneratedRpcInvoker(IGeneratedRpcDispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        /// <summary>只按 Slot 转发到生成的 switch；不使用 MethodInfo.Invoke。</summary>
        public Task<object> InvokeAsync(MethodDescriptor method, object instance, object[] boundArgs,
            CancellationToken ct)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));
            return _dispatcher.DispatchAsync(method.Slot, instance, boundArgs, ct);
        }
    }

    /// <summary>把生成分发表适配为 IMethodResolver。</summary>
    public sealed class GeneratedMethodResolver : IMethodResolver
    {
        private readonly IGeneratedRpcDispatcher _dispatcher;

        /// <summary>注入实现了 IGeneratedRpcDispatcher 的编译期生成类型。</summary>
        public GeneratedMethodResolver(IGeneratedRpcDispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        }

        /// <summary>交给生成代码的静态元数据表选择唯一方法。</summary>
        public bool TryResolve(string serviceName, string methodName, object[] args, out MethodDescriptor descriptor)
        {
            return _dispatcher.TryResolve(serviceName, methodName, args, out descriptor);
        }

        /// <summary>Mirror 式：按 MethodId 命中生成代码的 switch。</summary>
        public bool TryResolve(ulong methodId, object[] args, out MethodDescriptor descriptor)
        {
            return _dispatcher.TryResolve(methodId, args, out descriptor);
        }
    }
}