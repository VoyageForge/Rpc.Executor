namespace VoyageForge.Rpc.CodeGen
{
    /// <summary>DTO 序列化代码发射失败的原因分类。</summary>
    /// <remarks>
    /// 移植自试验包 Rpc.CodeGen.Probe。分类存在的意义：调用方要按类别选不同的诊断 ID，
    /// 而不是拼字符串 —— 字符串比较既容易拼错，也无法在编译期发现遗漏。
    /// </remarks>
    public enum RpcDtoEmitFailure
    {
        /// <summary>没有失败。</summary>
        None = 0,

        /// <summary>把 DTO 源码加入临时 Compilation 后，按 metadata name 取不到符号。</summary>
        SymbolNotFound = 1,

        /// <summary>当前编译里解析不到 MemoryPack 类型（通常是没引用 MemoryPack.Core.dll）。</summary>
        MemoryPackNotReferenced = 2,

        /// <summary>MemoryPack 的 <c>Validate()</c> 判定该类型不合法。</summary>
        ValidateFailed = 3,

        /// <summary>MemoryPack 的 <c>Emit()</c> 或前置解析抛出了异常。</summary>
        EmitThrew = 4,

        /// <summary>DTO 被 MemoryPack 判定为 <c>GenerateType.NoGenerate</c>。</summary>
        NoGenerate = 5,
    }
}
