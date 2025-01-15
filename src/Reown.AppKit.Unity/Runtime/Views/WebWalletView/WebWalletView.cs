using System;
using Reown.AppKit.Unity.Components;
using UnityEngine;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace Reown.AppKit.Unity.Views.WebWalletView
{
    public class WebWalletView : VisualElement
    {
        public const string Name = "web-wallet-view";

        private readonly Label _loadingLabel;
        private readonly VisualElement _buttons;
        private readonly Link _googleLocal;
        private readonly Link _googleDeploy;

        public event Action GoogleLocalClicked
        {
            add => _googleLocal.Clicked += value;
            remove => _googleLocal.Clicked -= value;
        }

        public event Action GoogleDeployClicked
        {
            add => _googleDeploy.Clicked += value;
            remove => _googleDeploy.Clicked -= value;
        }

        public new class UxmlFactory : UxmlFactory<WebWalletView>
        {
        }

        public WebWalletView() : this(null)
        {
        }

        public WebWalletView(string visualTreePath)
        {
            var asset = Resources.Load<VisualTreeAsset>(visualTreePath ?? "Reown/AppKit/Views/WebWalletView/WebWalletView");
            Debug.Log($"Is asset null? {asset == null}");
            asset.CloneTree(this);

            name = Name;

            _loadingLabel = this.Q<Label>("web-wallet-view__loading-label");
            _buttons = this.Q<VisualElement>("web-wallet-view__buttons");
            _googleLocal = this.Q<Link>("web-wallet-view__google-local");
            _googleDeploy = this.Q<Link>("web-wallet-view__google-deployment");

            Debug.Log("Is local google button null? " + (_googleLocal == null));

            ShowLoading();
        }

        public void ShowLoading()
        {
            _loadingLabel.style.display = DisplayStyle.Flex;
            _buttons.style.display = DisplayStyle.None;
        }

        public void ShowButtons()
        {
            _loadingLabel.style.display = DisplayStyle.None;
            _buttons.style.display = DisplayStyle.Flex;
        }
    }
}