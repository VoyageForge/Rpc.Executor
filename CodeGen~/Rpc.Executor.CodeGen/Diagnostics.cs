using Microsoft.CodeAnalysis;

namespace VoyageForge.Rpc.SourceGen
{
    /// <summary>
    /// 源生成器诊断：把“无法生成”的原因在编译期暴露给开发者，
    /// 而不是留到运行时才出现“命令怎么没注册”的静默失败。
    ///
    /// 诊断 ID 统一用 <c>RPCHANDLER</c> 前缀，便于在 Unity 控制台按前缀过滤。
    /// </summary>
    internal static class Diagnostics
    {
        /// <summary>同一请求类型注册了多个处理器（一个请求类型只能有一个处理器）。</summary>
        public static readonly DiagnosticDescriptor HandlerDuplicate = new DiagnosticDescriptor(
            id: "RPCHANDLER001",
            title: "请求类型存在多个处理器",
            messageFormat: "请求类型 '{0}' 已有处理器 '{1}'，重复的处理器 '{2}' 被忽略；一个请求类型只能有一个处理器。",
            category: "VoyageForge.Rpc",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}