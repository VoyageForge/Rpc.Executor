using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace VoyageForge.Rpc.Runtime.Handlers
{
    /// <summary>
    /// 处理器目录缓存：遍历已加载程序集的生成目录，按请求类型查找处理器。
    /// 查找结果缓存，重复查找不再遍历程序集。
    /// </summary>
    internal static class RpcHandlerCatalogCache
    {
        private static readonly Dictionary<Assembly, IRpcHandlerCatalog> Catalogs =
            new Dictionary<Assembly, IRpcHandlerCatalog>();

        private static readonly Dictionary<Type, Entry> Resolved = new Dictionary<Type, Entry>();
        private static readonly object Sync = new object();

        private readonly struct Entry
        {
            public readonly object Handler;
            public readonly bool IsAsync;

            public Entry(object handler, bool isAsync)
            {
                Handler = handler;
                IsAsync = isAsync;
            }
        }

        /// <summary>查找请求类型对应的处理器。</summary>
        public static bool TryGet(Type requestType, out object handler, out bool isAsync)
        {
            lock (Sync)
            {
                if (Resolved.TryGetValue(requestType, out Entry cached))
                {
                    handler = cached.Handler;
                    isAsync = cached.IsAsync;
                    return handler != null;
                }

                handler = null;
                isAsync = false;

                Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < assemblies.Length; i++)
                {
                    IRpcHandlerCatalog catalog = Get(assemblies[i]);
                    if (catalog == null)
                    {
                        continue;
                    }

                    if (catalog.TryGet(requestType, out handler, out isAsync))
                    {
                        Resolved[requestType] = new Entry(handler, isAsync);
                        return true;
                    }
                }

                Resolved[requestType] = new Entry(null, false);
                return false;
            }
        }

        private static IRpcHandlerCatalog Get(Assembly assembly)
        {
            if (assembly == null)
            {
                return null;
            }

            if (Catalogs.TryGetValue(assembly, out IRpcHandlerCatalog cached))
            {
                return cached;
            }

            IRpcHandlerCatalog catalog = null;

            try
            {
                object[] attributes = assembly.GetCustomAttributes(typeof(RpcHandlerCatalogAttribute), false);
                if (attributes != null && attributes.Length > 0 &&
                    attributes[0] is RpcHandlerCatalogAttribute marker &&
                    marker.CatalogType != null)
                {
                    catalog = Activator.CreateInstance(marker.CatalogType, true) as IRpcHandlerCatalog;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Rpc.Handlers] 读取程序集 {assembly.GetName().Name} 的处理器目录失败：{ex}");
            }

            Catalogs[assembly] = catalog;
            return catalog;
        }
    }
}