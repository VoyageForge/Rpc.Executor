using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace VoyageForge.Rpc.Samples.Tcp
{
    /// <summary>TCP 示例场景引导：按钮触发一次 Loopback 往返。</summary>
    public sealed class TcpBootstrap : MonoBehaviour
    {
        private void Start()
        {
            Button button = FindObjectOfType<Button>();
            if (button != null) button.onClick.AddListener(Run);
        }

        private async void Run()
        {
            await TcpRpcSample.RunLoopbackAsync(CancellationToken.None);
        }
    }
}