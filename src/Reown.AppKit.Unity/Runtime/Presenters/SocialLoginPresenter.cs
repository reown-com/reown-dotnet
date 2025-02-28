using System;
using System.Collections;
using Reown.AppKit.Unity.Profile;
using Reown.AppKit.Unity.Views.WebWalletView;
using Reown.Sign.Unity;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reown.AppKit.Unity
{
    public class SocialLoginPresenter : Presenter<SocialLoginView>
    {
        private WalletConnectConnectionProposal _connectionProposal;
        private readonly WaitForSecondsRealtime _waitForSeconds05;

        private const string DefaultWebWalletUrl = "https://chore-web-wallet-imp.appkit-web-wallet.pages.dev";

        private string _webWalletUrl;
        private string _providerName;

        public SocialLoginPresenter(RouterController router, VisualElement parent, bool hideView = true) : base(router, parent, hideView)
        {
            _waitForSeconds05 = new WaitForSecondsRealtime(0.5f);
        }

        protected override void OnVisibleCore()
        {
            base.OnVisibleCore();

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            _webWalletUrl = PlayerPrefs.GetString("RE_WEB_WALLET_URL", DefaultWebWalletUrl);
#else
            _webWalletUrl = DefaultWebWalletUrl;
#endif
            
            _providerName = PlayerPrefs.GetString("RE_SOCIAL_PROVIDER_NAME", "unknown");
            View.MainLabel.text = $"Log in with {_providerName}";
            View.MessageLabel.text = "Preparing to connect...";

            View.ProviderIcon.vectorImage = Resources.Load<VectorImage>($"Reown/AppKit/Images/{_providerName}");
            
            if (!AppKit.ConnectorController
                    .TryGetConnector<ProfileConnector>
                        (ConnectorType.Profile, out var connector))
                throw new Exception("No profiles connector"); // TODO: use custom exception

            _connectionProposal = (WalletConnectConnectionProposal)connector.Connect();

            UnityEventsDispatcher.Instance.StartCoroutine(OpenDeepLinkWhenReady());
        }

        private IEnumerator OpenDeepLinkWhenReady()
        {
            // Wait for transition to finish
            yield return _waitForSeconds05;

            if (string.IsNullOrWhiteSpace(_connectionProposal.Uri))
                yield return new WaitUntil(() => !string.IsNullOrWhiteSpace(_connectionProposal.Uri) || !IsVisible);

            View.MessageLabel.text = "Connecting to provider...";

            if (IsVisible)
                OpenWebWallet();
        }

        private void OpenWebWallet()
        {
            var deepLink = Linker.BuildConnectionDeepLink(_webWalletUrl, _connectionProposal.Uri);
            deepLink = $"{deepLink}&provider={_providerName}";
            Application.OpenURL(deepLink);
        }

        protected override void OnDisableCore()
        {
            base.OnDisableCore();
            _connectionProposal.Dispose();
        }
    }
}