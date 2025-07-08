using System;
using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Reown.AppKit.Unity.Tests;
using UnityEngine;
using UnityEngine.TestTools;

namespace Reown.AppKit.Unity.Test
{
    internal class WalletConnectTests : AppKitTestFixture
    {
        [UnityTest]
        public IEnumerator ShouldConnectAndDisconnect()
        {
            return UniTask.ToCoroutine(async () =>
            {
                try
                {
                    using var wallet = await WalletFixture.CreateWallet();

                    if (!AppKit.IsInitialized)
                    {
                        var tcs = new UniTaskCompletionSource();
                        AppKit.Initialized += (_, _) => tcs.TrySetResult();
                        await tcs.Task;
                    }

                    await UniTask.Delay(100);

                    Assert.That(AppKit.IsModalOpen, Is.False);
                    Assert.That(AppKit.IsAccountConnected, Is.False);

                    // Connect
                    await DappUi.TapConnectAsync();
                    Assert.That(AppKit.IsModalOpen, Is.True);

                    // Prepare a wallet
                    var accountConnectedTaskCompletionSource = new UniTaskCompletionSource();
                    AppKit.AccountConnected += (_, args) =>
                    {
                        Assert.That(args.Account.AccountId, Is.EqualTo($"eip155:1:{wallet.WalletAddress}"));
                        Assert.That(args.Account.Address, Is.EqualTo(wallet.WalletAddress));
                        Assert.That(args.Account.ChainId, Is.EqualTo("eip155:1"));
                        Assert.That(args.Accounts.Count(), Is.EqualTo(2));

                        accountConnectedTaskCompletionSource.TrySetResult();
                    };

                    var walletConnectionTaskCompletionSource = new UniTaskCompletionSource();
                    wallet.WalletKit.SessionProposed += async (sender, @event) =>
                    {
                        var id = @event.Id;
                        _ = await wallet.ApproveSession(id);
                        walletConnectionTaskCompletionSource.TrySetResult();
                    };

                    var uri = await AppKitUi.GetWalletConnectUriAsync();
                    await wallet.WalletKit.Pair(uri);

                    await walletConnectionTaskCompletionSource.Task;
                    await accountConnectedTaskCompletionSource.Task;

                    Assert.That(AppKit.IsModalOpen, Is.False);
                    Assert.That(AppKit.IsAccountConnected, Is.True);

                    await UniTask.Delay(300);

                    // Disconnect
                    var accountDisconnectedTaskCompletionSource = new UniTaskCompletionSource();
                    AppKit.AccountDisconnected += (_, _) =>
                    {
                        accountDisconnectedTaskCompletionSource.TrySetResult();
                    };

                    await DappUi.TapDisconnectAsync();
                    await accountDisconnectedTaskCompletionSource.Task;
                    Assert.That(AppKit.IsAccountConnected, Is.False);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            });
        }
    }
}