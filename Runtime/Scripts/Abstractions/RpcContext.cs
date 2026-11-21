using System;
using System.Collections.Generic;

namespace VoyageForge.Rpc.Executor
{
    /// <summary>
    /// 执行器层调用上下文：从 RpcRequest.Headers 解析（由传输层把会话信息写入请求头）。
    /// 注意：SessionId 属于传输层概念，这里不暴露；执行器层只关心身份与权限。
    /// </summary>
    public class RpcContext
    {
        /// <summary>调用方用户 ID；未提供时为 null。</summary>
        public string UserId { get; set; }

        /// <summary>调用方权限集合（区分大小写，按字符串匹配）。</summary>
        public IReadOnlyList<string> Permissions { get; set; } = Array.Empty<string>();

        /// <summary>是否已通过身份识别。</summary>
        public bool IsAuthenticated => !string.IsNullOrEmpty(UserId);

        /// <summary>请求头（只读副本）。</summary>
        public IReadOnlyDictionary<string, string> Headers { get; set; } =
            new Dictionary<string, string>();

        /// <summary>从请求头构建上下文：user-id 与 permissions（逗号分隔）。</summary>
        public static RpcContext FromHeaders(IDictionary<string, string> headers)
        {
            RpcContext context = new RpcContext();
            if (headers == null) return context;

            context.Headers = new Dictionary<string, string>(headers);
            if (headers.TryGetValue("user-id", out string userId)) context.UserId = userId;
            if (headers.TryGetValue("permissions", out string permissions) && !string.IsNullOrEmpty(permissions))
            {
                context.Permissions = permissions.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            }

            return context;
        }

        /// <summary>是否拥有指定权限。</summary>
        public bool HasPermission(string permission)
        {
            if (string.IsNullOrEmpty(permission)) return true;
            foreach (string item in Permissions)
            {
                if (string.Equals(item.Trim(), permission, StringComparison.Ordinal)) return true;
            }

            return false;
        }
    }
}