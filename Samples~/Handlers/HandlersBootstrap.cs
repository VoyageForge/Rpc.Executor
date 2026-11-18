using UnityEngine;
using UnityEngine.UI;

namespace VoyageForge.Rpc.Samples.Handlers
{
    /// <summary>Handlers 示例场景引导：按钮触发一次强类型处理器调用。</summary>
    public sealed class HandlersBootstrap : MonoBehaviour
    {
        private void Start()
        {
            Button button = FindObjectOfType<Button>();
            if (button != null) button.onClick.AddListener(Run);
        }

        private void Run()
        {
            Debug.Log("[HandlersSample] 点击：处理器由 RpcHandlerGenerator 自动注册");
        }
    }
}