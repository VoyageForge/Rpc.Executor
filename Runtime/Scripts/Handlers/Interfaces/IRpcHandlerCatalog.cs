using System;

namespace VoyageForge.Rpc.Runtime.Handlers
{
    /// <summary>
    /// 程序集级处理器目录：把“请求类型 → 处理器实例”编译期写死。
    /// 运行时由 <see cref="RpcHandlerCatalogCache"/> 读取并缓存。
    /// </summary>
    public interface IRpcHandlerCatalog
    {
        /// <summary>查找请求类型对应的处理器。</summary>
        /// <param name="requestType">请求类型。</param>
        /// <param name="handler">处理器实例。</param>
        /// <param name="isAsync">是否为异步处理器。</param>
        /// <returns>是否找到。</returns>
        bool TryGet(Type requestType, out object handler, out bool isAsync);
    }
}