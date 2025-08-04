using System;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using Reown.AppKit.Unity.Components;
using Reown.AppKit.Unity.Tests;
using Reown.Core.Common.Model.Errors;
using Reown.Core.Network.Models;
using UnityEngine;
using UnityEngine.TestTools;

// ReSharper disable AccessToDisposedClosure

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
                    Debug.Log($"[{nameof(ShouldConnectAndDisconnect)}] Creating wallet...");
                    using var wallet = await WalletFixture.CreateWallet();

                    Debug.Log($"[{nameof(ShouldConnectAndDisconnect)}] Waiting for AppKit...");
                    await WaitForAppKitAsync();

                    Debug.Log($"[{nameof(ShouldConnectAndDisconnect)}] Verifying initial state...");
                    Assert.That(AppKit.IsModalOpen, Is.False);
                    Assert.That(AppKit.IsAccountConnected, Is.False);

                    // Connect
                    Debug.Log($"[{nameof(ShouldConnectAndDisconnect)}] Tapping connect button...");
                    await DappUi.TapConnectAsync();
                    Assert.That(AppKit.IsModalOpen, Is.True);

                    // Prepare a wallet
                    Debug.Log($"[{nameof(ShouldConnectAndDisconnect)}] Setting up wallet connection handlers...");
                    var accountConnectedTaskCompletionSource = new UniTaskCompletionSource();
                    AppKit.AccountConnected += (_, args) =>
                    {
                        Debug.Log($"[{nameof(ShouldConnectAndDisconnect)}] Account connected with address: {args.Account.Address}");
                        Assert.That(args.Account.AccountId, Is.EqualTo($"eip155:1:{wallet.WalletAddress}"));
                        Assert.That(args.Account.Address, Is.EqualTo(wallet.WalletAddress));
                        Assert.That(args.Account.ChainId, Is.EqualTo("eip155:1"));
                        Assert.That(args.Accounts.Count(), Is.EqualTo(2));

                        accountConnectedTaskCompletionSource.TrySetResult();
                    };

                    var walletConnectionTaskCompletionSource = new UniTaskCompletionSource();
                    wallet.WalletKit.SessionProposed += async (sender, @event) =>
                    {
                        Debug.Log($"[{nameof(ShouldConnectAndDisconnect)}] Session proposed with ID: {@event.Id}");
                        var id = @event.Id;
                        _ = await wallet.ApproveSession(id);
                        Debug.Log($"[{nameof(ShouldConnectAndDisconnect)}] Session approved");
                        walletConnectionTaskCompletionSource.TrySetResult();
                    };

                    Debug.Log($"[{nameof(ShouldConnectAndDisconnect)}] Getting WalletConnect URI...");
                    var uri = await AppKitUi.GetWalletConnectUriAsync();
                    Debug.Log($"[{nameof(ShouldConnectAndDisconnect)}] Pairing with wallet...");
                    await wallet.WalletKit.Pair(uri);

                    Debug.Log($"[{nameof(ShouldConnectAndDisconnect)}] Waiting for connection completion...");
                    await walletConnectionTaskCompletionSource.Task;
                    await accountConnectedTaskCompletionSource.Task;

                    Debug.Log($"[{nameof(ShouldConnectAndDisconnect)}] Verifying connected state...");
                    Assert.That(AppKit.IsModalOpen, Is.False);
                    Assert.That(AppKit.IsAccountConnected, Is.True);

                    await UniTask.Delay(300);

                    // Disconnect
                    Debug.Log($"[{nameof(ShouldConnectAndDisconnect)}] Setting up disconnect handler...");
                    var accountDisconnectedTaskCompletionSource = new UniTaskCompletionSource();
                    AppKit.AccountDisconnected += (_, _) =>
                    {
                        Debug.Log($"[{nameof(ShouldConnectAndDisconnect)}] Account disconnected");
                        accountDisconnectedTaskCompletionSource.TrySetResult();
                    };

                    var walletDisconnectedTaskCompletionSource = new UniTaskCompletionSource();
                    wallet.WalletKit.SessionDeleted += (sender, @event) =>
                    {
                        Debug.Log($"[{nameof(ShouldConnectAndDisconnect)}] Wallet session deleted with ID: {@event.Id}");
                        walletDisconnectedTaskCompletionSource.TrySetResult();
                    };

                    Debug.Log($"[{nameof(ShouldConnectAndDisconnect)}] Tapping disconnect button...");
                    await DappUi.TapDisconnectAsync();

                    await UniTask.WhenAll(accountDisconnectedTaskCompletionSource.Task, walletDisconnectedTaskCompletionSource.Task);
                    
                    Debug.Log($"[{nameof(ShouldConnectAndDisconnect)}] Verifying disconnected state...");
                    Assert.That(AppKit.IsAccountConnected, Is.False);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[{nameof(ShouldConnectAndDisconnect)}] Test failed with exception: {e}");
                    Debug.LogException(e);
                }
            });
        }

        [UnityTest]
        public IEnumerator ShouldReject()
        {
            return UniTask.ToCoroutine(async () =>
            {
                Debug.Log($"[{nameof(ShouldReject)}] Creating wallet...");
                using var wallet = await WalletFixture.CreateWallet();

                Debug.Log($"[{nameof(ShouldReject)}] Waiting for AppKit...");
                await WaitForAppKitAsync();

                Debug.Log($"[{nameof(ShouldReject)}] Tapping connect button...");
                await DappUi.TapConnectAsync();

                Debug.Log($"[{nameof(ShouldReject)}] Setting up connection handlers...");
                var accountConnectedTaskCompletionSource = new UniTaskCompletionSource();
                AppKit.AccountConnected += (_, args) => accountConnectedTaskCompletionSource.TrySetResult();

                var walletConnectionTaskCompletionSource = new UniTaskCompletionSource();
                const ErrorType rejectErrorType = ErrorType.DISAPPROVED_CHAINS;
                wallet.WalletKit.SessionProposed += async (sender, @event) =>
                {
                    Debug.Log($"[{nameof(ShouldReject)}] Session proposed with ID: {@event.Id}");
                    var id = @event.Id;
                    await wallet.WalletKit.RejectSession(id, Error.FromErrorType(rejectErrorType));
                    Debug.Log($"[{nameof(ShouldReject)}] Session rejected with error type: {rejectErrorType}");
                    walletConnectionTaskCompletionSource.TrySetResult();
                };

                Debug.Log($"[{nameof(ShouldReject)}] Getting WalletConnect URI...");
                var uri = await AppKitUi.GetWalletConnectUriAsync();
                Debug.Log($"[{nameof(ShouldReject)}] Pairing with wallet...");
                await wallet.WalletKit.Pair(uri);

                Debug.Log($"[{nameof(ShouldReject)}] Waiting for connection rejection...");
                await walletConnectionTaskCompletionSource.Task;

                Debug.Log($"[{nameof(ShouldReject)}] Validating error message...");
                await AppKitUi.ValidateSnackbarAsync(true, SdkErrors.MessageFromType(rejectErrorType));
            });
        }
    }
}