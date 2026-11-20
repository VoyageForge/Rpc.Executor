using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VoyageForge.Rpc.Executor.Abstractions;
using VoyageForge.Rpc.Transport;

namespace VoyageForge.Rpc.Executor
{
    /// <summary>RPC 执行器统一入口：把请求路由到服务对象、绑定参数、执行生成的分发代码并返回响应。</summary>
    /// <remarks>
    /// RpcExecutor 不关心请求从哪里来。Console、TCP、测试代码都只需构造 RpcRequest 并调用 ExecuteAsync。
    /// 解析方法名和真实调用方法被拆成 IMethodResolver / IInvoker 两个可替换点。
    /// </remarks>
    public sealed class RpcExecutor
    {
        /// <summary>服务名到业务实例的线程安全注册表。</summary>
        private readonly ServiceRegistry _services;

        /// <summary>将服务名/方法名解析为编译期生成的方法描述符。</summary>
        private readonly IMethodResolver _resolver;

        /// <summary>通过描述符 Slot 调用生成的静态分发器。</summary>
        private readonly IInvoker _invoker;

        /// <summary>在真正进入业务方法前转换每一个原始参数。</summary>
        private readonly IArgumentConverter _converter;

        /// <summary>幂等缓存：默认开启，按 RequestId 去重成功响应。</summary>
        private readonly IdempotencyCache _idempotency;

        /// <summary>构造时复制的拦截器快照，调用过程中顺序不会被外部改动。</summary>
        private readonly IReadOnlyList<IRpcInterceptor> _interceptors;

        /// <summary>
        /// MethodId → 可读名称的解析器（由生成器 RpcMethodNames 注入）；默认 null 表示无名称表。
        /// </summary>
        public Func<ulong, string> MethodNameResolver { get; set; }

        /// <summary>是否向 RpcResponse.Detail 暴露完整异常堆栈；默认 false。</summary>
        /// <summary>(ServiceName, MethodName) → MethodId 缓存：服务端计算并复用，客户端不需要知道 MethodId。</summary>
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ulong> _methodIdCache =
            new System.Collections.Concurrent.ConcurrentDictionary<string, ulong>(StringComparer.Ordinal);

        /// <summary>MethodId 计算器（与生成器/调用端同一 FNV-1a 64 规范）。</summary>
        private readonly Fnv1A64MethodIdProvider _methodIdProvider = new Fnv1A64MethodIdProvider();

        public bool ExposeExceptionDetail { get; set; }

        /// <summary>创建 RPC 执行器并注入所有必要协作者。</summary>
        public RpcExecutor(ServiceRegistry services, IMethodResolver resolver, IInvoker invoker,
            IArgumentConverter converter = null, IEnumerable<IRpcInterceptor> interceptors = null,
            IdempotencyCache idempotency = null)
        {
            _idempotency = idempotency ?? new IdempotencyCache();
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
            _converter = converter ?? new JsonArgumentConverter();

            // ToArray 创建独立快照，避免调用期间调用方修改原始 List 导致拦截顺序变化。
            _interceptors = (interceptors ?? Array.Empty<IRpcInterceptor>()).ToArray();
        }

        /// <summary>执行一个 RPC 请求，并把拦截器异常也统一转换为 RpcResponse。</summary>
        public async Task<RpcResponse> ExecuteAsync(RpcRequest request,
            CancellationToken ct = default(CancellationToken))
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            // 幂等命中：同一 RequestId 的重复投递直接返回缓存响应，不重复执行处理器。
            if (request.RequestId != 0 && _idempotency.TryGet(request.RequestId, out RpcResponse cached))
            {
                return cached;
            }

            try
            {
                // 从最内层 Core 开始，倒序包裹拦截器：列表第一个最终处于最外层。
                // A、B 的注册顺序会得到 A.Before -> B.Before -> Core -> B.After -> A.After。
                Func<Task<RpcResponse>> next = () => ExecuteCoreAsync(request, ct);
                for (int index = _interceptors.Count - 1; index >= 0; index--)
                {
                    IRpcInterceptor current = _interceptors[index];
                    Func<Task<RpcResponse>> following = next;
                    next = () => current.InvokeAsync(request, following, ct);
                }

                RpcResponse response = await next().ConfigureAwait(false);
                if (response != null && response.Success)
                {
                    _idempotency.Set(request.RequestId, response);
                }

                return response;
            }
            catch (OperationCanceledException)
            {
                return RpcResponse.Fail("CANCELLED", "请求已取消。");
            }
            catch (Exception exception)
            {
                // ExecuteCoreAsync 处理业务调用错误；这里处理拦截器、管道委托等外层异常。
                return RpcResponse.Fail(exception.GetType().FullName, exception.Message,
                    ExposeExceptionDetail ? exception.ToString() : null);
            }
        }

        /// <summary>
        /// 泛型执行：返回 RpcResponse&lt;T&gt;，业务代码直接拿强类型 Data，无需再转换 Result。
        /// </summary>
        public async Task<RpcResponse<TResponse>> ExecuteAsync<TResponse>(RpcRequest request,
            CancellationToken ct = default(CancellationToken))
        {
            RpcResponse baseResponse = await ExecuteAsync(request, ct).ConfigureAwait(false);

            RpcResponse<TResponse> typed = new RpcResponse<TResponse>
            {
                Success = baseResponse.Success,
                Error = baseResponse.Error,
                RequestId = baseResponse.RequestId,
                MethodId = baseResponse.MethodId,
                Detail = baseResponse.Detail
            };

            if (baseResponse.Success)
            {
                // 生成器把返回值直接序列化进 Payload（零装箱）；这里反序列化回强类型。
                if (baseResponse.Payload != null && baseResponse.Payload.Length > 0)
                {
                    typed.Data = RpcPayload.Unpack<TResponse>(baseResponse.Payload);
                }
                else if (baseResponse.Result is TResponse value)
                {
                    typed.Data = value;
                }
            }

            return typed;
        }

        /// <summary>按描述符参数类型计算 MethodId（排除 CancellationToken）。</summary>
        private ulong ComputeMethodId(string serviceName, string methodName, MethodDescriptor method)
        {
            Type[] parameterTypes = method.ParameterTypes ?? Type.EmptyTypes;
            string[] argTypes = new string[parameterTypes.Length];
            int count = 0;
            for (int index = 0; index < parameterTypes.Length; index++)
            {
                Type type = parameterTypes[index];
                if (type == typeof(CancellationToken)) continue;
                argTypes[count++] = type.FullName ?? type.Name;
            }

            if (count != argTypes.Length) Array.Resize(ref argTypes, count);
            return _methodIdProvider.GetMethodId(serviceName, methodName, argTypes);
        }

        /// <summary>执行核心步骤：取消检查、查服务、选方法、绑定参数、静态调用、包装结果。</summary>
        private async Task<RpcResponse> ExecuteCoreAsync(RpcRequest request, CancellationToken ct)
        {
            try
            {
                // 1. 请求已经取消时，不再查服务、不转换参数、更不进入业务代码。
                ct.ThrowIfCancellationRequested();

                // 2. 先按服务名查找实例。未注册属于框架错误，不是业务异常。
                // 默认只传 MethodId：名称缺失时从生成器名称表还原，便于诊断。
                string resolvedName = request.MethodName ?? (MethodNameResolver != null ? MethodNameResolver(request.MethodId) : null);
                string resolvedService = request.ServiceName;
                if (resolvedService == null && resolvedName != null)
                {
                    int dot = resolvedName.IndexOf('.');
                    resolvedService = dot > 0 ? resolvedName.Substring(0, dot) : resolvedName;
                }

                if (!_services.TryGet(resolvedService, out object instance))
                    return RpcResponse.Fail("SERVICE_NOT_FOUND", "服务不存在：" + (resolvedService ?? "<MethodId=0x" + request.MethodId.ToString("X16") + ">"));


                // 3. 生成式解析器按服务、方法和原始值选择唯一重载。方法实际调用仍在后续的 Invoker 中完成。
                MethodDescriptor method;
                try
                {
                    // Mirror 式：MethodId 优先（生成器 switch 命中），为 0 时回退字符串。
                    bool resolved = request.MethodId != 0
                        ? _resolver.TryResolve(request.MethodId, Array.Empty<object>(), out method)
                        : _resolver.TryResolve(request.ServiceName, request.MethodName, Array.Empty<object>(), out method);
                    if (!resolved)
                        return RpcResponse.Fail("METHOD_NOT_FOUND",
                            "方法不存在或参数无法匹配：" + request.ServiceName + "." + request.MethodName +
                            " (MethodId=0x" + request.MethodId.ToString("X16") + ")");
                }
                catch (RpcMethodAmbiguousException exception)
                {
                    return RpcResponse.Fail("METHOD_AMBIGUOUS", exception.Message);
                }

                // 3.5 MethodId：客户端只给 ServiceName + MethodName，这里计算并缓存；请求里已带则直接用。
                ulong methodId = request.MethodId;
                if (methodId == 0)
                {
                    string cacheKey = request.ServiceName + "\u001F" + request.MethodName;
                    methodId = _methodIdCache.GetOrAdd(cacheKey,
                        _ => ComputeMethodId(request.ServiceName, request.MethodName, method));
                }

                // 6. 在真正进入业务方法前再次检查取消，避免绑定期间被取消的请求仍继续执行。
                ct.ThrowIfCancellationRequested();

                // 7. Invoker 通过 Slot 进入生成代码；同步、Task、Task<T> 统一成 Task<object>。
                // 执行器层上下文：从请求头解析身份与权限（SessionId 属于传输层，不进入执行器）。
                RpcContext rpcContext = RpcContext.FromHeaders(request.Headers);
                if (!string.IsNullOrEmpty(method.RequiredPermission) &&
                    !rpcContext.HasPermission(method.RequiredPermission))
                {
                    return RpcResponse.Fail("UNAUTHORIZED", "缺少权限：" + method.RequiredPermission);
                }

                // 控制器基类：注入上下文（UserId/Permissions/Headers）。
                if (instance is RpcControllerBase controller) controller.SetContext(rpcContext);

                byte[] responsePayload = await _invoker.InvokeAsync(method, instance, request.Payload, ct).ConfigureAwait(false);

                // 8. 方法执行期间也可能取消；检查后再返回成功响应。
                ct.ThrowIfCancellationRequested();
                return RpcResponse.Ok(responsePayload);
            }
            catch (OperationCanceledException)
            {
                return RpcResponse.Fail("CANCELLED", "请求已取消。");
            }
            catch (Exception exception)
            {
                // 业务异常使用异常全名作为 Error.Code，堆栈默认不对外返回。
                return RpcResponse.Fail(exception.GetType().FullName, exception.Message,
                    ExposeExceptionDetail ? exception.ToString() : null);
            }
        }
    }
}
