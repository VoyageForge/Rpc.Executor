using UnityEngine;
using UnityEngine.UI;

namespace VoyageForge.Rpc.Samples
{
    /// <summary>Calculator 示例场景引导：按钮触发一次加法示例。</summary>
    public sealed class CalculatorBootstrap : MonoBehaviour
    {
        private void Start()
        {
            Button button = FindObjectOfType<Button>();
            if (button != null) button.onClick.AddListener(async () =>
            {
                await CalculatorSample.RunAsync();
            });
        }
    }
}