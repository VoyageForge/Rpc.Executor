using VoyageForge.Rpc.Executor.Abstractions;

namespace VoyageForge.Rpc.Executor
{
    /// <summary>
    /// 权限策略：决定当前上下文能否调用某个方法。
    /// 默认实现检查 MethodDescriptor.RequiredPermission；可替换为 RBAC/ABAC/资源级策略。
    /// </summary>
    public interface IRpcAuthorizationPolicy
    {
        /// <summary>是否允许调用。</summary>
        bool IsAuthorized(RpcContext context, MethodDescriptor method);
    }
}