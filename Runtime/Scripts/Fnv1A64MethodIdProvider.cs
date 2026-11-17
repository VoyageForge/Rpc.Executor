using System;
using System.Collections.Generic;
using System.Text;
using Rpc.Executor.Abstractions;

namespace Rpc.Executor
{
    /// <summary>Runtime 侧的默认 FNV-1a 64 位方法 ID 计算器。</summary>
    /// <remarks>
    /// CodeGen 和 Runtime 必须遵守同一条 key 规范：
    /// v1 + U+001F + ServiceName + U+001F + MethodName + U+001F + ArgTypeFullName...
    /// 算法、编码、分隔符和版本前缀都是协议的一部分，已发布后不可随意改变；改变时应创建 v2。
    /// </remarks>
    public sealed class Fnv1A64MethodIdProvider : IMethodIdProvider
    {
        /// <summary>方法标识输入规范版本。</summary>
        private const string KeyVersion = "v1";
        /// <summary>不可见分隔符 U+001F，避免名称和参数类型直接拼接产生边界歧义。</summary>
        private const char UnitSeparator = '\u001F';
        /// <summary>FNV-1a 64 位 offset basis 常量。</summary>
        private const ulong OffsetBasis = 14695981039346656037UL;
        /// <summary>FNV-1a 64 位 prime 常量。</summary>
        private const ulong Prime = 1099511628211UL;

        /// <summary>根据服务名、方法名和参数类型全名计算稳定方法 ID。</summary>
        public ulong GetMethodId(string serviceName, string methodName, IReadOnlyList<string> argTypes)
        {
            if (string.IsNullOrWhiteSpace(serviceName)) throw new ArgumentException("服务名不能为空。", nameof(serviceName));
            if (string.IsNullOrWhiteSpace(methodName)) throw new ArgumentException("方法名不能为空。", nameof(methodName));

            // 先构建规范化 key。参数类型必须使用完整身份名，如 System.Int32，而不是 int 或 Int32。
            StringBuilder key = new StringBuilder();
            key.Append(KeyVersion).Append(UnitSeparator)
                .Append(serviceName).Append(UnitSeparator)
                .Append(methodName).Append(UnitSeparator);

            if (argTypes != null)
            {
                for (int index = 0; index < argTypes.Count; index++)
                {
                    if (index > 0) key.Append(UnitSeparator);
                    key.Append(argTypes[index] ?? string.Empty);
                }
            }

            // 使用 UTF-8 明确固定字节序列，不能使用 Encoding.Default（不同机器可能不同）。
            byte[] bytes = Encoding.UTF8.GetBytes(key.ToString());
            ulong hash = OffsetBasis;
            for (int index = 0; index < bytes.Length; index++)
            {
                hash ^= bytes[index];
                hash *= Prime;
            }
            return hash;
        }
    }
}
