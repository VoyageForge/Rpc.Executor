namespace VoyageForge.Rpc.CodeGen
{
    /// <summary>DTO 上一个属性对应的参数信息。</summary>
    /// <remarks>
    /// 移植自试验包 Rpc.CodeGen.Probe。字段在构造时全部算好，
    /// 避免发射阶段再做符号查询（发射阶段拿不到 Compilation 之外的信息）。
    /// </remarks>
    public sealed class RpcDtoParameter
    {
        /// <summary>构造一个参数模型。</summary>
        /// <param name="parameterName">契约里的原始参数名，用于注释与诊断。</param>
        /// <param name="propertyName">转成 PascalCase 后的属性名。</param>
        /// <param name="typeDisplay">参数完整类型名（<c>global::</c> 前缀），直接写进属性声明。</param>
        /// <param name="isReferenceType">是否引用类型：是则补 <c>= default!;</c> 避免 CS8618。</param>
        public RpcDtoParameter(string parameterName, string propertyName, string typeDisplay, bool isReferenceType)
        {
            ParameterName = parameterName;
            PropertyName = propertyName;
            TypeDisplay = typeDisplay;
            IsReferenceType = isReferenceType;
        }

        /// <summary>契约里的原始参数名。</summary>
        public string ParameterName { get; }

        /// <summary>DTO 上对应的属性名（PascalCase）。</summary>
        public string PropertyName { get; }

        /// <summary>DTO 上对应属性的类型（完整限定名）。</summary>
        public string TypeDisplay { get; }

        /// <summary>该参数类型是否为引用类型。</summary>
        public bool IsReferenceType { get; }
    }
}
