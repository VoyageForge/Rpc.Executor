using System;

namespace VoyageForge.Rpc.Runtime.Handlers
{
    /// <summary>程序集级标记：指向源生成器为该程序集生成的 <see cref="IRpcHandlerCatalog"/> 实现。</summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
    public sealed class RpcHandlerCatalogAttribute : Attribute
    {
        /// <summary>目录类型。</summary>
        public Type CatalogType { get; }

        /// <summary>构造标记。</summary>
        /// <param name="catalogType">目录类型。</param>
        public RpcHandlerCatalogAttribute(Type catalogType)
        {
            CatalogType = catalogType ?? throw new ArgumentNullException(nameof(catalogType));
        }
    }
}