using System;
using System.Collections.Concurrent;

namespace VoyageForge.Rpc.Executor
{
    public abstract class ControllerBase { }

    public sealed class LocalExecutor
    {
        private readonly ConcurrentDictionary<Type, Func<ControllerBase>> _factories =
            new ConcurrentDictionary<Type, Func<ControllerBase>>();

        public void Register<T>(Func<T> factory) where T : ControllerBase
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (!_factories.TryAdd(typeof(T), factory))
                throw new InvalidOperationException("Controller already registered: " + typeof(T));
        }

        public T Local<T>() where T : ControllerBase
        {
            if (!_factories.TryGetValue(typeof(T), out var factory))
                throw new InvalidOperationException("Unregistered controller: " + typeof(T));
            return (T)(factory() ?? throw new InvalidOperationException("Controller factory returned null: " + typeof(T)));
        }
    }
}
