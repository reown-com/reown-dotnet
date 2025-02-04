using System;
using Reown.AppKit.Unity.Profile;
using Reown.AppKit.Unity.Views.WebWalletView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reown.AppKit.Unity
{
    public class WebWalletPresenter : Presenter<WebWalletView>
    {
        private WalletConnectConnectionProposal _connectionProposal;

        public WebWalletPresenter(RouterController router, VisualElement parent, bool hideView = true) : base(router, parent, hideView)
        {
            View.GoogleLocalClicked += OnGoogleLocalClicked;
            View.XLocalClicked += OnXLocalClicked;
            View.GoogleDeployClicked += OnGoogleDeployClicked;
            View.XDeployClicked += OnXDeployClicked;
        }

        protected override void OnVisibleCore()
        {
            base.OnVisibleCore();

            if (!AppKit.ConnectorController
                    .TryGetConnector<ProfileConnector>
                        (ConnectorType.Profile, out var connector))
                throw new Exception("No profiles connector"); // TODO: use custom exception

            _connectionProposal = (WalletConnectConnectionProposal)connector.Connect();

            _connectionProposal.ConnectionUpdated += ConnectionUpdatedHandler;
        }

        private void ConnectionUpdatedHandler(ConnectionProposal obj)
        {
            View.ShowButtons();
        }

        private void OnGoogleLocalClicked()
        {
            var encodedWcUrl = Uri.EscapeDataString(_connectionProposal.Uri);
            var url = $"http://localhost:5173/wc?uri={encodedWcUrl}&provider=google";
            Debug.Log($"Opening URL: {url}");
            Application.OpenURL(url);
        }

        private void OnXLocalClicked()
        {
            var encodedWcUrl = Uri.EscapeDataString(_connectionProposal.Uri);
            var url = $"http://localhost:5173/wc?uri={encodedWcUrl}&provider=x";
            Debug.Log($"Opening URL: {url}");
            Application.OpenURL(url);
        }

        private void OnGoogleDeployClicked()
        {
            var encodedWcUrl = Uri.EscapeDataString(_connectionProposal.Uri);
            var url = $"https://web-wallet-app-omega.vercel.app/wc?uri={encodedWcUrl}&provider=google";
            Debug.Log($"Opening URL: {url}");
            Application.OpenURL(url);
        }

        private void OnXDeployClicked()
        {
            var encodedWcUrl = Uri.EscapeDataString(_connectionProposal.Uri);
            var url = $"https://web-wallet-app-omega.vercel.app/wc?uri={encodedWcUrl}&provider=x";
            Debug.Log($"Opening URL: {url}");
            Application.OpenURL(url);
        }
    }
}