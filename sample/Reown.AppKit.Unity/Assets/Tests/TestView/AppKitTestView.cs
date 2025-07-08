using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Reown.AppKit.Unity.Components;
using Reown.AppKit.Unity.Tests;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reown.AppKit.Unity.Test
{
    public class AppKitTestView : TestView
    {
        public AppKitTestView(VisualElement root) : base(root)
        {
        }

        public async UniTask OpenWalletConnectAsync()
        {
            var wcListItem = Q<ListItem>("walletconnect-list-item");
            Assert.IsNotNull(wcListItem, "WalletConnect list item not found");

            await UtkUtils.TapAsync(wcListItem);
        }

        public async UniTask<string> GetWalletConnectUriAsync()
        {
            await OpenWalletConnectAsync();

            var qrCode = Q<QrCode>();
            Assert.IsNotNull(qrCode);

            // Wait for WalletConnect pairing url to be assigned to the QR code view
            Assert.That(qrCode.Data, Is.Empty);
            await UniTask.WaitUntilValueChanged(qrCode, qr => qr.Data);
            Assert.That(qrCode.Data, Does.StartWith("wc:"));

            // Snackbar is invisible
            var snackbar = Q<Snackbar>();
            Assert.That(Mathf.Approximately(snackbar.style.opacity.value, 0), Is.True);

            var copyLink = Q<VisualElement>("qrcode-view__copy-link");
            Assert.IsNotNull(copyLink, "Copy link button not found");

            // Hit Copy Link
            await UtkUtils.TapAsync(copyLink);

            // Snackbar is visible
            await UniTask.Delay(100);
            Assert.That(Mathf.Approximately(snackbar.style.opacity.value, 0), Is.Not.True);

            // Pairing URL has been copied
            Assert.That(GUIUtility.systemCopyBuffer, Is.EqualTo(qrCode.Data));

            return GUIUtility.systemCopyBuffer;
        }
    }
}