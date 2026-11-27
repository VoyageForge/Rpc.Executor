using System;
using System.Collections.Concurrent;

namespace VoyageForge.Rpc.Executor
{
    /// <summary>本地执行器可以注册和取得的 Controller 基类。</summary>
    public abstract class ControllerBase { }

    /// <summary>按具体 Controller 类型保存工厂，每次请求实例时调用对应工厂。</summary>
    public sealed class LocalExecutor
    {
        private readonly ConcurrentDictionary<Type, Func<ControllerBase>> _factories =
            new ConcurrentDictionary<Type, Func<ControllerBase>>();

        /// <summary>登记创建函数。同一类型只允许注册一次。</summary>
        public void Register<T>(Func<T> factory) where T : ControllerBase
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (!_factories.TryAdd(typeof(T), factory))
                throw new InvalidOperationException("Controller already registered: " + typeof(T));
        }

        /// <summary>取得本次调用使用的实例。工厂决定复用已有对象还是创建新对象。</summary>
        public T Local<T>() where T : ControllerBase
        {
            if (!_factories.TryGetValue(typeof(T), out var factory))
                throw new InvalidOperationException("Unregistered controller: " + typeof(T));
            return (T)(factory() ?? throw new InvalidOperationException("Controller factory returned null: " + typeof(T)));
        }
    }
}
