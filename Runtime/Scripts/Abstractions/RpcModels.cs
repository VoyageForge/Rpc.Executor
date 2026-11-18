using System;

namespace VoyageForge.Rpc.Executor.Abstractions
{
    /// <summary>服务注册策略。</summary>
    public sealed class RegisterOptions
    {
        /// <summary>重复注册同名服务时是否抛异常；false 表示默认覆盖旧实例。</summary>
        public bool ThrowOnDuplicate { get; set; }
    }

    /// <summary>
    /// 由源代码生成器在编译期输出的方法元数据。RpcExecutor 不需要在运行时扫描 MethodInfo。
    /// </summary>
    /// <remarks>
    /// Slot 是生成器在当前编译产物中的 switch 编号；MethodId 是根据稳定签名计算出的 64 位值。
    /// 不要把 Slot 存入跨版本网络协议：新增方法可能改变 Slot。跨程序集/跨进程标识应使用 MethodId。
    /// </remarks>
    public sealed class MethodDescriptor
    {
        /// <summary>该方法所属的服务名。</summary>
        public string ServiceName { get; set; }

        /// <summary>对外暴露的方法名称。</summary>
        public string MethodName { get; set; }

        /// <summary>由 v1 + U+001F 分隔的服务名、方法名和参数类型全名计算出的 FNV-1a 64 位标识。</summary>
        public ulong MethodId { get; set; }

        /// <summary>当前生成分发表中对应的 switch case 编号。</summary>
        public int Slot { get; set; }

        /// <summary>
        /// 方法真实参数类型，包含 CancellationToken（如果方法声明了它）。RpcRequest.Parameters 不含此参数。
        /// </summary>
        public Type[] ParameterTypes { get; set; }

        /// <summary>方法是否返回 Task 或 Task&lt;T&gt;。</summary>
        public bool IsAsync { get; set; }

        /// <summary>同步返回类型或 Task&lt;T&gt; 的 T；无返回值时是 typeof(void)。</summary>
        public Type ResultType { get; set; }
    }
}