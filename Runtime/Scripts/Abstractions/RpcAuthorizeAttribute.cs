using System;

namespace VoyageForge.Rpc.Executor
{
    /// <summary>
    /// 方法级授权：执行器在调用业务方法前检查 RpcContext.Permissions 是否包含指定权限。
    /// 未通过返回 UNAUTHORIZED，不进入业务代码。
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
    public sealed class RpcAuthorizeAttribute : Attribute
    {
        /// <summary>所需权限名；为空表示只要求已认证。</summary>
        public string Permission { get; }

        /// <summary>创建授权标记。</summary>
        public RpcAuthorizeAttribute(string permission = null)
        {
            Permission = permission;
        }
    }
}