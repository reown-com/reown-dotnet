using System;
using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Reown.AppKit.Unity.Components;
using Reown.AppKit.Unity.Tests;
using Reown.Sign.Models;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace Reown.AppKit.Unity.Test
{
    internal class WalletConnectTests : AppKitTestFixture
    {
        [UnityTest]
        public IEnumerator ConnectWalletToAppKitDesktop()
        {
            return UniTask.ToCoroutine(async () =>
            {
                try
                {
                    var cryptoWallet = new CryptoWalletFixture();
                    var walletKit = await CreateWalletKitInstance();

                    if (!AppKit.IsInitialized)
                    {
                        var tcs = new UniTaskCompletionSource();
                        AppKit.Initialized += (_, _) => tcs.TrySetResult();
                        await tcs.Task;
                    }

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

                    // Connect a wallet
                    var accountConnectedTaskCompletionSource = new UniTaskCompletionSource();
                    AppKit.AccountConnected += (_, args) =>
                    {
                        Assert.That(args.Account.AccountId, Is.EqualTo($"eip155:1:{cryptoWallet.WalletAddress}"));
                        Assert.That(args.Account.Address, Is.EqualTo(cryptoWallet.WalletAddress));
                        Assert.That(args.Account.ChainId, Is.EqualTo("eip155:1"));
                        Assert.That(args.Accounts.Count(), Is.EqualTo(2));

                        accountConnectedTaskCompletionSource.TrySetResult();
                    };

                    var testNamespaces = new Namespaces()
                        .WithNamespace("eip155", new Namespace()
                            .WithChain("eip155:1")
                            .WithChain("eip155:10")
                            .WithMethod("personal_sign")
                            .WithAccount($"eip155:1:{cryptoWallet.WalletAddress}")
                            .WithAccount($"eip155:10:{cryptoWallet.WalletAddress}")
                        );

                    var walletConnectionTaskCompletionSource = new UniTaskCompletionSource();
                    walletKit.SessionProposed += async (sender, @event) =>
                    {
                        var id = @event.Id;
                        _ = await walletKit.ApproveSession(id, testNamespaces);
                        walletConnectionTaskCompletionSource.TrySetResult();
                    };

                    var uri = GUIUtility.systemCopyBuffer;
                    await walletKit.Pair(uri);

                    await walletConnectionTaskCompletionSource.Task;
                    await accountConnectedTaskCompletionSource.Task;

                    Assert.That(AppKit.IsModalOpen, Is.False);
                    Assert.That(AppKit.IsAccountConnected, Is.True);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            });
        }
    }
}