using System;
using Reown.AppKit.Unity.Utils;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reown.AppKit.Unity.Components
{
    public class NetworkButton : VisualElement, IDisposable
    {
        public const string Name = "network-button";
        public static readonly string NameIcon = $"{Name}__icon";
        public static readonly string NameText = $"{Name}__name";
        public static readonly string NameChevron = $"{Name}__chevron";

        private bool _showName = true;
        private bool _showChevron = true;
        private bool _showBorder = true;
        private bool _disposed;

        private RemoteSprite<Image> _networkIcon;
        private Clickable _clickable;

        public bool ShowName
        {
            get => _showName;
            set
            {
                _showName = value;
                NetworkName.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public bool ShowChevron
        {
            get => _showChevron;
            set
            {
                _showChevron = value;
                NetworkChevron.style.display = value ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public bool ShowBorder
        {
            get => _showBorder;
            set
            {
                _showBorder = value;
                EnableInClassList("border", value);
            }
        }

        public Clickable Clickable
        {
            get => _clickable;
            set
            {
                _clickable = value;
                this.AddManipulator(value);
            }
        }

        public readonly Image NetworkIcon;
        public readonly Label NetworkName;
        public readonly Image NetworkChevron;

        public new class UxmlFactory : UxmlFactory<NetworkButton, UxmlTraits>
        {
        }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            private readonly UxmlBoolAttributeDescription _showName = new()
            {
                name = "show-name",
                defaultValue = true
            };

            private readonly UxmlBoolAttributeDescription _showChevron = new()
            {
                name = "show-chevron",
                defaultValue = true
            };

            private readonly UxmlBoolAttributeDescription _showBorder = new()
            {
                name = "show-border",
                defaultValue = true
            };

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var networkButton = (NetworkButton)ve;

                networkButton.ShowName = _showName.GetValueFromBag(bag, cc);
                networkButton.ShowChevron = _showChevron.GetValueFromBag(bag, cc);
                networkButton.ShowBorder = _showBorder.GetValueFromBag(bag, cc);
            }
        }

        public NetworkButton()
        {
            var asset = Resources.Load<VisualTreeAsset>("Reown/AppKit/Components/NetworkButton/NetworkButton");
            asset.CloneTree(this);

            name = Name;
            AddToClassList(Name);

            NetworkIcon = this.Q<Image>(NameIcon);
            NetworkName = this.Q<Label>(NameText);
            NetworkChevron = this.Q<Image>(NameChevron);

            ShowBorder = true;
            focusable = true;

            AppKit.NetworkController.ChainChanged += ChainChangedHandler;

            // Update network data when button becomes visible
            // ReSharper disable once HeapView.CanAvoidClosure
#if UNITY_6000_0_OR_NEWER
            RegisterCallbackOnce<GeometryChangedEvent>(_ => UpdateNetworkButton(AppKit.NetworkController.ActiveChain));
#else
            RegisterCallback<GeometryChangedEvent>(_ => UpdateNetworkButton(AppKit.NetworkController.ActiveChain));
#endif
            

            Clickable = new Clickable(() => AppKit.OpenModal(ViewType.NetworkSearch));
        }

        private void ChainChangedHandler(object sender, NetworkController.ChainChangedEventArgs e)
        {
            UpdateNetworkButton(e.NewChain);
        }

        private void UpdateNetworkButton(Chain chain)
        {
            if (chain == null)
            {
                NetworkName.text = "Network";
                NetworkIcon.style.display = DisplayStyle.None;
                return;
            }

            NetworkName.text = chain.Name;

            var newNetworkIcon = RemoteSpriteFactory.GetRemoteSprite<Image>(chain.ImageUrl);
            _networkIcon?.UnsubscribeImage(NetworkIcon);
            _networkIcon = newNetworkIcon;
            _networkIcon.SubscribeImage(NetworkIcon);
            NetworkIcon.style.display = DisplayStyle.Flex;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            AppKit.NetworkController.ChainChanged -= ChainChangedHandler;
            _networkIcon?.UnsubscribeImage(NetworkIcon);

            _disposed = true;
        }
    }
}