using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Reown.AppKit.Unity.Tests;
using UnityEngine.UIElements;

namespace Reown.AppKit.Unity.Test
{
    public class DappTestView : TestView
    {
        public DappTestView(VisualElement root) : base(root)
        {
        }

        public async UniTask TapConnectAsync()
        {
            await TapButtonAsync("connect-btn");
        }

        public async UniTask TapDisconnectAsync()
        {
            await TapButtonAsync("disconnect-btn");
        }

        private async UniTask TapButtonAsync(string name)
        {
            var button = Root.Q<Button>(name);

            Assert.That(button, Is.Not.Null, $"Button '{name}' not found.");
            Assert.That(button.enabledSelf, Is.True, $"Button '{name}' is not enabled.");

            await UtkUtils.TapAsync(button);
            await UniTask.Delay(200);
        }
    }
}