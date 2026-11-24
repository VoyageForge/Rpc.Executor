using System.Collections.Generic;

namespace VoyageForge.Rpc.CodeGen
{
    /// <summary>DTO 上一个属性对应的参数信息。</summary>
    /// <remarks>
    /// <para>
    /// 字段在构造时全部算好，避免发射阶段再做符号查询（发射阶段拿不到 Compilation 之外的信息）。
    /// </para>
    /// <para>
    /// <b>类型用短名</b>（<c>int</c> 而不是 <c>global::System.Int32</c>）：
    /// 生成的 DTO 是给人看、也给人 debug 的，全限定名会把一行属性声明撑成 80 字符以上。
    /// 短名的解析依赖生成文件头部的 <c>using</c>，命名空间集合由
    /// <see cref="RpcDtoFactory"/> 从参数类型里收集。
    /// </para>
    /// </remarks>
    public sealed class RpcDtoParameter
    {
        /// <summary>构造一个参数模型。</summary>
        /// <param name="parameterName">契约里的原始参数名，用于注释与诊断。</param>
        /// <param name="propertyName">转成 PascalCase 后的属性名。</param>
        /// <param name="typeDisplay">参数类型的<b>短名</b>（如 <c>int</c>、<c>List&lt;UserInfo&gt;</c>）。</param>
        /// <param name="isReferenceType">是否引用类型：是则补 <c>= default!;</c> 避免 CS8618。</param>
        /// <param name="order">该属性在序列化布局中的序号（从 0 开始，= 排序后的位置）。</param>
        public RpcDtoParameter(
            string parameterName,
            string propertyName,
            string typeDisplay,
            bool isReferenceType,
            int order)
        {
            ParameterName = parameterName;
            PropertyName = propertyName;
            TypeDisplay = typeDisplay;
            IsReferenceType = isReferenceType;
            Order = order;
        }

        /// <summary>契约里的原始参数名。</summary>
        public string ParameterName { get; }

        /// <summary>DTO 上对应的属性名（PascalCase）。</summary>
        public string PropertyName { get; }

        /// <summary>DTO 上对应属性的类型<b>短名</b>。</summary>
        public string TypeDisplay { get; }

        /// <summary>该参数类型是否为引用类型。</summary>
        public bool IsReferenceType { get; }

        /// <summary>序列化序号（从 0 开始，等于按参数名排序后的位置）。</summary>
        public int Order { get; }
    }
}
