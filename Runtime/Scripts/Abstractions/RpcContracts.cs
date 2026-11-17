using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using VoyageForge.Rpc.Transport;

namespace Rpc.Executor.Abstractions
{
    /// <summary>把请求中的原始对象转换为业务方法所需 CLR 类型的策略接口。</summary>
    public interface IArgumentConverter
    {
        /// <summary>转换失败返回 false，并通过 error 给出参数索引之外的类型转换原因。</summary>
        bool TryConvert(object value, Type targetType, out object converted, out string error);
    }

    /// <summary>根据服务名、方法名和原始参数选择唯一的编译期方法描述符。</summary>
    public interface IMethodResolver
    {
        /// <summary>无匹配返回 false；存在同等匹配的重载时应抛出 RpcMethodAmbiguousException。</summary>
        bool TryResolve(string serviceName, string methodName, object[] args, out MethodDescriptor descriptor);
    }

    /// <summary>
    /// 整体参数绑定扩展点。默认 RpcExecutor 直接使用 IArgumentConverter 逐项绑定；
    /// 复杂场景可以实现 IArgumentBinder，将校验、默认值、参数映射和转换统一封装。
    /// </summary>
    public interface IArgumentBinder
    {
        /// <summary>绑定成功返回 true；失败通过 RpcError 返回 ARGUMENT_BIND_ERROR 等结构化错误。</summary>
        bool TryBind(MethodDescriptor method, object[] rawArgs, CancellationToken ct,
            out object[] boundArgs, out RpcError error);
    }

    /// <summary>真正执行方法调用的策略接口。</summary>
    public interface IInvoker
    {
        /// <summary>接收已经完成参数绑定的数组，将同步结果、Task 和 Task&lt;T&gt; 统一转换成 Task&lt;object&gt;。</summary>
        Task<object> InvokeAsync(MethodDescriptor method, object instance, object[] boundArgs, CancellationToken ct);
    }

    /// <summary>RPC 拦截器接口，多个拦截器按注册顺序组成洋葱模型。</summary>
    public interface IRpcInterceptor
    {
        /// <summary>调用 next 继续进入内层；不调用 next 可直接返回响应，实现认证拒绝或缓存短路。</summary>
        Task<RpcResponse> InvokeAsync(RpcRequest request, Func<Task<RpcResponse>> next, CancellationToken ct);
    }

    /// <summary>请求指标接口，预留给计数器、Prometheus 或 Unity Profiler 适配器。</summary>
    public interface IRpcMetrics
    {
        /// <summary>请求开始。</summary>
        void OnRequestStart(RpcRequest request);

        /// <summary>请求完成。</summary>
        void OnRequestComplete(RpcRequest request, RpcResponse response, TimeSpan elapsed);

        /// <summary>请求处理过程中发生异常。</summary>
        void OnRequestFailed(RpcRequest request, Exception exception);
    }

    /// <summary>链路追踪接口。</summary>
    public interface IRpcTracer
    {
        /// <summary>创建一个 span；返回对象 Dispose 时结束 span。</summary>
        IDisposable BeginSpan(string operationName, RpcRequest request);
    }

    /// <summary>调度扩展点，允许 Host 把业务调用派发到主线程或隔离工作队列。</summary>
    public interface IRpcScheduler
    {
        /// <summary>在目标调度器上执行异步工作。</summary>
        Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken ct);
    }

    /// <summary>MethodId 算法扩展点。默认协议固定使用 FNV-1a 64 位 v1。</summary>
    public interface IMethodIdProvider
    {
        /// <summary>根据服务名、方法名和参数类型全名计算稳定标识；CancellationToken 不包含在 argTypes。</summary>
        ulong GetMethodId(string serviceName, string methodName, IReadOnlyList<string> argTypes);
    }

    /// <summary>方法描述提供者扩展点；生成式默认路径无需运行时反射扫描。</summary>
    public interface IMethodDescriptorProvider
    {
        /// <summary>获取指定契约类型暴露的方法描述。</summary>
        IEnumerable<MethodDescriptor> GetMethods(Type contractType);
    }

    /// <summary>服务发现扩展点。</summary>
    public interface IServiceDiscovery
    {
        /// <summary>解析服务名对应的 endpoints。</summary>
        Task<IReadOnlyList<ServiceEndpoint>> ResolveAsync(string serviceName, CancellationToken ct);

        /// <summary>向发现系统注册 endpoint。</summary>
        Task RegisterAsync(ServiceEndpoint endpoint, CancellationToken ct);
    }

    /// <summary>负载均衡扩展点。</summary>
    public interface ILoadBalancer
    {
        /// <summary>从可用 endpoint 中选择本次请求目标。</summary>
        ServiceEndpoint Pick(IReadOnlyList<ServiceEndpoint> endpoints, RpcRequest request);
    }

    /// <summary>重试策略扩展点。</summary>
    public interface IRetryPolicy
    {
        /// <summary>决定是否重试，并通过 delay 输出下次尝试前的等待时间。</summary>
        bool ShouldRetry(RpcResponse response, int attempt, out TimeSpan delay);
    }

    /// <summary>超时策略扩展点。</summary>
    public interface ITimeoutPolicy
    {
        /// <summary>返回本次调用的超时时长；null 表示不额外设置超时。</summary>
        TimeSpan? GetTimeout(RpcRequest request, MethodDescriptor method);
    }

    /// <summary>认证扩展点。</summary>
    public interface IRpcAuthenticator
    {
        /// <summary>验证请求身份并返回认证结果。</summary>
        Task<AuthResult> AuthenticateAsync(RpcRequest request, CancellationToken ct);
    }

    /// <summary>授权扩展点。</summary>
    public interface IRpcAuthorizer
    {
        /// <summary>判断指定身份是否有权调用目标方法。</summary>
        Task<bool> AuthorizeAsync(AuthResult identity, MethodDescriptor method, CancellationToken ct);
    }

    /// <summary>服务发现结果的最小数据模型。</summary>
    public sealed class ServiceEndpoint
    {
        /// <summary>逻辑服务名。</summary>
        public string ServiceName { get; set; }

        /// <summary>由具体传输层解释的地址。</summary>
        public string Address { get; set; }
    }

    /// <summary>认证结果的最小模型，不绑定特定身份系统。</summary>
    public sealed class AuthResult
    {
        /// <summary>身份验证是否通过。</summary>
        public bool Succeeded { get; set; }

        /// <summary>认证主体，例如用户 ID、ClaimsPrincipal 或自定义对象。</summary>
        public object Identity { get; set; }
    }
}