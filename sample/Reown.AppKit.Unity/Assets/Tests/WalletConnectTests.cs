using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Reown.AppKit.Unity.Components;
using Reown.AppKit.Unity.Tests;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace Reown.AppKit.Unity.Test
{
    internal class WalletConnectTests : AppKitTestFixture
    {
        [UnityTest]
        public IEnumerator OpenAndCloseAppKit()
        {
            return UniTask.ToCoroutine(async () =>
            {
                try
                {
                    var tcs = new UniTaskCompletionSource();
                    AppKit.Initialized += (_, _) => tcs.TrySetResult();
                    await tcs.Task;

                    await UniTask.Delay(100);

                    Assert.That(AppKit.IsModalOpen, Is.False);
                    Assert.That(AppKit.IsAccountConnected, Is.False);

                    var connectButton = DappUi.Q<Button>("connect-btn");

                    await UtkUtils.TapAsync(connectButton);
                    await UniTask.Delay(300);

                    Assert.That(AppKit.IsModalOpen, Is.True);

                    var wcListItem = AppKitUi.Q<ListItem>("walletconnect-list-item");
                    Assert.IsNotNull(wcListItem, "WalletConnect list item not found");

                    await UtkUtils.TapAsync(wcListItem);

                    var qrCode = AppKitUi.Q<QrCode>();
                    Assert.IsNotNull(qrCode);

                    // Wait for WalletConnect pairing url to be assigned to the QR code view
                    Assert.That(qrCode.Data, Is.Empty);
                    await UniTask.WaitUntilValueChanged(qrCode, qr => qr.Data);
                    Assert.That(qrCode.Data, Does.StartWith("wc:"));

                    // Snackbar is invisible
                    var snackbar = AppKitUi.Q<Snackbar>();
                    Assert.That(Mathf.Approximately(snackbar.style.opacity.value, 0), Is.True);

                    var copyLink = AppKitUi.Q<VisualElement>("qrcode-view__copy-link");
                    Assert.IsNotNull(copyLink, "Copy link button not found");

                    // Hit Copy Link
                    await UtkUtils.TapAsync(copyLink);

                    // Snackbar is visible
                    await UniTask.Delay(100);
                    Assert.That(Mathf.Approximately(snackbar.style.opacity.value, 0), Is.Not.True);

                    // Pairing URL has been copied
                    Assert.That(GUIUtility.systemCopyBuffer, Is.EqualTo(qrCode.Data));
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            });
        }
    }
}