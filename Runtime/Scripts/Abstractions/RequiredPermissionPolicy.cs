using VoyageForge.Rpc.Executor.Abstractions;

namespace VoyageForge.Rpc.Executor
{
    /// <summary>默认权限策略：方法声明了 RequiredPermission 时，要求上下文包含该权限。</summary>
    public sealed class RequiredPermissionPolicy : IRpcAuthorizationPolicy
    {
        /// <summary>检查上下文权限集合。</summary>
        public bool IsAuthorized(RpcContext context, MethodDescriptor method)
        {
            if (method == null || string.IsNullOrEmpty(method.RequiredPermission)) return true;
            return context != null && context.HasPermission(method.RequiredPermission);
        }
    }
}