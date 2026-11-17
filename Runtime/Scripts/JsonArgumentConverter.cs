using System;
using System.Globalization;
using Rpc.Executor.Abstractions;

namespace Rpc.Executor
{
    /// <summary>默认参数转换器：支持基础类型、Nullable、枚举、Guid、TimeSpan 和可选 JsonElement 输入。</summary>
    /// <remarks>
    /// 为了不让 RPC Runtime 强制引用 System.Text.Json 程序集，本类通过类型全名识别 JsonElement，
    /// 再读取它的 ValueKind / GetString / ToString。这里的反射仅用于读取 JSON 节点值，
    /// 不会用 MethodInfo.Invoke 调用 RPC 业务方法；业务方法分发始终使用 Source Generator 的 switch。
    /// 如果你的项目已经统一把 JSON 反序列化成 int/string 等 CLR 对象，就不会走 JsonElement 分支。
    /// </remarks>
    public sealed class JsonArgumentConverter : IArgumentConverter
    {
        /// <summary>尝试把单个原始参数转换为生成器记录的目标 CLR 类型。</summary>
        public bool TryConvert(object value, Type targetType, out object converted, out string error)
        {
            converted = null;
            error = null;
            if (targetType == null)
            {
                error = "目标参数类型不能为空。";
                return false;
            }

            try
            {
                // Nullable<T> 的实际转换目标是底层 T，但转换结果仍可以赋给 Nullable<T>。
                Type nullableType = Nullable.GetUnderlyingType(targetType);
                Type actualType = nullableType ?? targetType;

                // null 只允许赋给引用类型或 Nullable<T>；普通值类型必须明确报错。
                if (value == null)
                {
                    if (!actualType.IsValueType || nullableType != null) return true;
                    error = "不能将 null 转换为 " + targetType.FullName;
                    return false;
                }

                // 输入已经是目标类型时直接返回，避免把 int 再转成 int 或丢失精度。
                if (actualType.IsInstanceOfType(value))
                {
                    converted = value;
                    return true;
                }

                // 可选解包：先把 JsonElement 变成基础 CLR 表达，再复用下面统一的转换逻辑。
                if (IsJsonElement(value))
                {
                    if (!TryUnwrapJsonElement(value, out value, out error)) return false;
                    if (value == null) return TryConvert(null, targetType, out converted, out error);
                    if (actualType.IsInstanceOfType(value))
                    {
                        converted = value;
                        return true;
                    }
                }

                // 枚举支持名称（忽略大小写）和数值输入。
                if (actualType.IsEnum)
                {
                    converted = value is string enumName
                        ? Enum.Parse(actualType, enumName, true)
                        : Enum.ToObject(actualType, value);
                    return true;
                }

                // Guid 与 TimeSpan 不实现 IConvertible，因此单独处理。
                if (actualType == typeof(Guid))
                {
                    converted = Guid.Parse(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return true;
                }

                if (actualType == typeof(TimeSpan))
                {
                    converted = TimeSpan.Parse(Convert.ToString(value, CultureInfo.InvariantCulture),
                        CultureInfo.InvariantCulture);
                    return true;
                }

                // 其余基础类型使用 InvariantCulture，避免操作系统区域设置影响协议解释。
                converted = Convert.ChangeType(value, actualType, CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception exception)
            {
                // 所有转换异常都变成 false + error；调用方将返回 ARGUMENT_BIND_ERROR，不会进入业务方法。
                converted = null;
                error = exception.Message;
                return false;
            }
        }

        /// <summary>判断对象是否来自 System.Text.Json 的 JsonElement，但不在编译期引用该程序集。</summary>
        private static bool IsJsonElement(object value)
        {
            return value != null && string.Equals(value.GetType().FullName, "System.Text.Json.JsonElement",
                StringComparison.Ordinal);
        }

        /// <summary>
        /// 将 JsonElement 提取为 string、bool 或数字文本。
        /// 使用反射读取可选程序集的 API，确保 Unity 项目即使没引用 System.Text.Json 仍能编译 Runtime。
        /// </summary>
        private static bool TryUnwrapJsonElement(object element, out object value, out string error)
        {
            value = null;
            error = null;
            try
            {
                Type elementType = element.GetType();
                object kindValue = elementType.GetProperty("ValueKind").GetValue(element, null);
                string kind = Convert.ToString(kindValue, CultureInfo.InvariantCulture);

                if (kind == "Null" || kind == "Undefined") return true;
                if (kind == "True")
                {
                    value = true;
                    return true;
                }

                if (kind == "False")
                {
                    value = false;
                    return true;
                }

                if (kind == "String")
                {
                    value = elementType.GetMethod("GetString", Type.EmptyTypes).Invoke(element, null);
                    return true;
                }

                // Number、数组和对象由 JsonElement.ToString 提供 JSON 文本；基础参数只会接受可解析的标量文本。
                value = element.ToString();
                return true;
            }
            catch (Exception exception)
            {
                error = "读取 JsonElement 失败：" + exception.Message;
                return false;
            }
        }
    }
}