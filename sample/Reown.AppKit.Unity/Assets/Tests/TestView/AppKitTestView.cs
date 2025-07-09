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

        public async UniTask ValidateSnackbarAsync(bool visible, string message = null)
        {
            await UniTask.Delay(50);

            var snackbar = Q<Snackbar>();
            Assert.IsNotNull(snackbar, "Snackbar not found");

            Assert.That(Mathf.Approximately(snackbar.style.opacity.value, 0), Is.Not.EqualTo(visible));

            if (!string.IsNullOrWhiteSpace(message))
                Assert.That(snackbar.Message, Is.EqualTo(message));
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
            await ValidateSnackbarAsync(false);

            var copyLink = Q<VisualElement>("qrcode-view__copy-link");
            Assert.IsNotNull(copyLink, "Copy link button not found");

            // Hit Copy Link
            await UtkUtils.TapAsync(copyLink);

            // Snackbar is visible
            await ValidateSnackbarAsync(true);

            // Pairing URL has been copied
            Assert.That(GUIUtility.systemCopyBuffer, Is.EqualTo(qrCode.Data));

            return GUIUtility.systemCopyBuffer;
        }
    }
}