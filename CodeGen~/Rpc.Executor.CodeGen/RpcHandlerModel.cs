using Microsoft.CodeAnalysis;

namespace VoyageForge.Rpc.SourceGen
{
    /// <summary>处理器模型：请求类型 → 处理器类型（同步/异步）。</summary>
    internal sealed class RpcHandlerModel
    {
        /// <summary>请求类型全名（生成代码用）。</summary>
        public string RequestCodeName { get; }

        /// <summary>处理器类型全名（生成代码用）。</summary>
        public string HandlerCodeName { get; }

        /// <summary>是否异步处理器。</summary>
        public bool IsAsync { get; }

        /// <summary>诊断位置。</summary>
        public Location Location { get; }

        public RpcHandlerModel(string requestCodeName, string handlerCodeName, bool isAsync, Location location)
        {
            RequestCodeName = requestCodeName;
            HandlerCodeName = handlerCodeName;
            IsAsync = isAsync;
            Location = location;
        }
    }
}