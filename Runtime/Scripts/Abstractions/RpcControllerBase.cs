using VoyageForge.Rpc.Transport;

namespace VoyageForge.Rpc.Executor
{
    /// <summary>
    /// 服务端控制器基类（WebAPI 的 ControllerBase 形态）：
    /// 提供 Context（UserId/Permissions，不含 SessionId）与 Ok/Fail 辅助方法。
    /// </summary>
    public abstract class RpcControllerBase
    {
        /// <summary>当前调用上下文（由执行器在 dispatch 前注入）。</summary>
        protected RpcContext Context { get; private set; } = new RpcContext();

        /// <summary>是否已通过身份识别。</summary>
        protected bool IsAuthenticated => Context.IsAuthenticated;

        /// <summary>执行器注入上下文；业务代码不要调用。</summary>
        internal void SetContext(RpcContext context)
        {
            Context = context ?? new RpcContext();
        }

        /// <summary>构造成功响应。</summary>
        protected RpcResponse<T> Ok<T>(T data)
        {
            return new RpcResponse<T> { Data = data };
        }

        /// <summary>构造成功响应（无数据）。</summary>
        protected RpcResponse Ok()
        {
            return new RpcResponse();
        }

        /// <summary>构造失败响应。</summary>
        protected RpcResponse Fail(string code, string message)
        {
            return RpcResponse.Fail(code, message);
        }
    }
}