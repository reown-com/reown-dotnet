using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Assertions;

namespace Reown.AppKit.Unity
{
    public class SiweController
    {
        public bool IsEnabled
        {
            get => Config is { Enabled: true };
        }

        public SiweConfig Config
        {
            get => AppKit.Config.siweConfig;
        }

        public const string SessionPlayerPrefsKey = "RE_SIWE_SESSION";

        public SiweController()
        {
            if (AppKit.Config.siweConfig?.GetMessageParams == null)
            {
                throw new InvalidOperationException("GetMessageParams function is required in SiweConfig.");
            }

            AppKit.AccountDisconnected += AccountDisconnectedHandler;
            AppKit.ChainChanged += ChainChangedHandler;
            AppKit.AccountChanged += AccountChangedHandler;
        }

        public async ValueTask<string> GetNonceAsync()
        {
            if (Config.GetNonce != null)
            {
                return await Config.GetNonce();
            }

            return SiweUtils.GenerateNonce();
        }

        public async ValueTask<SiweMessage> CreateMessageAsync(string ethAddress, string ethChainId)
        {
            var nonce = await GetNonceAsync();
            var messageParams = AppKit.Config.siweConfig.GetMessageParams();

            var createMessageArgs = new SiweCreateMessageArgs(messageParams)
            {
                Nonce = nonce,
                Address = ethAddress,
                ChainId = ethChainId
            };

            var message = Config.CreateMessage != null
                ? Config.CreateMessage(createMessageArgs)
                : SiweUtils.FormatMessage(createMessageArgs);

            return new SiweMessage
            {
                Message = message,
                CreateMessageArgs = createMessageArgs
            };
        }

        public async ValueTask<bool> VerifyMessageAsync(SiweVerifyMessageArgs args)
        {
            if (Config.VerifyMessage != null)
            {
                return await Config.VerifyMessage(args);
            }

            return await args.Cacao.VerifySignature(AppKit.Config.projectId);
        }

        public async ValueTask<SiweSession> GetSessionAsync(GetSiweSessionArgs args)
        {
            Assert.IsTrue(Array.TrueForAll(args.ChainIds, chainId => !Core.Utils.IsValidChainId(chainId)), "Chain IDs must be Ethereum chain IDs.");
            Assert.IsFalse(Core.Utils.IsValidAccountId(args.Address), "Address must be an Ethereum address.");

            SiweSession session = null;
            if (Config.GetSession != null)
            {
                session = await Config.GetSession(args);
            }
            else
            {
                session = new SiweSession(args);
            }

            Debug.Log($"[SiweController] Session is null: {session == null}");
            var json = JsonConvert.SerializeObject(session);
            Debug.Log($"[SiweController] Session JSON: {json}");
            Debug.Log($"Thread id: {Thread.CurrentThread.ManagedThreadId}");
            PlayerPrefs.SetString(SessionPlayerPrefsKey, json);
            // PlayerPrefs.Save();
            Debug.Log("[SiweController] Session saved to PlayerPrefs.");

            Config.OnSignInSuccess(session);

            return session;
        }

        public async ValueTask DisconnectAsync()
        {
            Debug.Log("[SiweController] Delete session from PlayerPrefs.");
            PlayerPrefs.DeleteKey(SessionPlayerPrefsKey);

            if (Config.SignOut != null)
            {
                await Config.SignOut();
            }

            Debug.Log("[SiweController] Sign out success.");
            Config.OnSignOutSuccess();
        }

        private async void AccountDisconnectedHandler(object sender, Connector.AccountDisconnectedEventArgs e)
        {
            if (IsEnabled && Config.SignOutOnWalletDisconnect)
            {
                await DisconnectAsync();
            }
        }

        private async void ChainChangedHandler(object sender, NetworkController.ChainChangedEventArgs e)
        {
            if (!IsEnabled || !Config.SignOutOnChainChange)
                return;

            var siweSessionJson = PlayerPrefs.GetString(SessionPlayerPrefsKey);
            if (string.IsNullOrWhiteSpace(siweSessionJson))
                return;

            var siweSession = JsonConvert.DeserializeObject<SiweSession>(siweSessionJson);
            if (!siweSession.EthChainIds.Contains(e.NewChain.ChainReference))
            {
                await DisconnectAsync();
                // TODO: request signature instead
                await AppKit.DisconnectAsync();
            }
        }

        private async void AccountChangedHandler(object sender, Connector.AccountChangedEventArgs e)
        {
            if (!IsEnabled || !Config.SignOutOnAccountChange)
                return;

            var siweSessionJson = PlayerPrefs.GetString(SessionPlayerPrefsKey);
            if (string.IsNullOrWhiteSpace(siweSessionJson))
                return;

            var siweSession = JsonConvert.DeserializeObject<SiweSession>(siweSessionJson);

            if (!string.Equals(siweSession.EthAddress, e.Account.Address, StringComparison.InvariantCultureIgnoreCase))
            {
                await DisconnectAsync();
                // TODO: request signature instead
                await AppKit.DisconnectAsync();
            }
        }
    }
}