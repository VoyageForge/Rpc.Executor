using System;
using System.Collections.Concurrent;
using Rpc.Executor.Abstractions;

namespace Rpc.Executor
{
    /// <summary>线程安全的服务实例注册表，保存服务名到服务对象的映射。</summary>
    /// <remarks>
    /// 注册表只管理本进程里的对象实例；它不是服务发现系统，也不会创建实例或扫描业务方法。
    /// ConcurrentDictionary 只保证字典操作线程安全，不会自动保证服务对象自身的业务状态线程安全。
    /// </remarks>
    public sealed class ServiceRegistry
    {
        /// <summary>
        /// 使用 Ordinal 比较器让服务名大小写敏感，并避免不同操作系统区域设置影响匹配结果。
        /// value 统一使用 object，真正调用时由生成分发器转为对应服务契约类型。
        /// </summary>
        private readonly ConcurrentDictionary<string, object> _services =
            new ConcurrentDictionary<string, object>(StringComparer.Ordinal);

        /// <summary>注册一个服务实例。</summary>
        /// <typeparam name="TContract">调用契约类型，建议传接口而不是实现类。</typeparam>
        /// <param name="instance">真正执行业务方法的对象。</param>
        /// <param name="name">显式服务名；优先级最高。为空时读取 RpcServiceAttribute，再回退类型全名。</param>
        /// <param name="options">重复注册策略；不传时默认覆盖旧实例。</param>
        public void Register<TContract>(TContract instance, string name = null, RegisterOptions options = null)
            where TContract : class
        {
            if (instance == null) throw new ArgumentNullException(nameof(instance));

            // 第一步：确定服务名。显式 name 的优先级最高，允许同一个接口用不同逻辑名称注册。
            string serviceName = name;
            if (string.IsNullOrWhiteSpace(serviceName))
            {
                // 这里只读取类型上的 Attribute 元数据，不扫描方法，也不以反射调用业务方法。
                RpcServiceAttribute attribute =
                    Attribute.GetCustomAttribute(typeof(TContract), typeof(RpcServiceAttribute))
                        as RpcServiceAttribute;

                serviceName = attribute != null && !string.IsNullOrWhiteSpace(attribute.Name)
                    ? attribute.Name
                    : (typeof(TContract).FullName ?? typeof(TContract).Name);
            }

            if (string.IsNullOrWhiteSpace(serviceName))
                throw new ArgumentException("服务名不能为空。", nameof(name));

            // 第二步：根据配置决定重复注册策略。
            RegisterOptions actualOptions = options ?? new RegisterOptions();
            if (actualOptions.ThrowOnDuplicate)
            {
                // TryAdd 是原子操作；多线程同时注册时，只有一个线程能成功。
                if (!_services.TryAdd(serviceName, instance))
                    throw new InvalidOperationException("服务已注册：" + serviceName);
            }
            else
            {
                // ConcurrentDictionary 的索引器写入线程安全；并发覆盖时最后一次成功写入生效。
                _services[serviceName] = instance;
            }
        }

        /// <summary>查找服务实例。name 为空时直接返回 false，不把无效名称传给字典。</summary>
        public bool TryGet(string name, out object instance)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                instance = null;
                return false;
            }

            return _services.TryGetValue(name, out instance);
        }
    }

    /// <summary>声明 RPC 契约以及默认服务名。可标在接口或类上。</summary>
    [AttributeUsage(AttributeTargets.Interface | AttributeTargets.Class, Inherited = false)]
    public sealed class RpcServiceAttribute : Attribute
    {
        /// <summary>默认服务名；null/空白时注册表会使用契约类型全名。</summary>
        public string Name { get; }

        /// <summary>声明服务。显式 Register(instance, name) 可以覆盖这里的默认名称。</summary>
        public RpcServiceAttribute(string name = null)
        {
            Name = name;
        }
    }
}