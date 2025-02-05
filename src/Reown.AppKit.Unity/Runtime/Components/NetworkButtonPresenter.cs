using Reown.AppKit.Unity.Utils;
using UnityEngine.UIElements;

namespace Reown.AppKit.Unity.Components
{
    public class NetworkButtonPresenter
    {
        private readonly NetworkButton _networkButton;
        private RemoteSprite<Image> _networkIcon;
        private bool _disposed;

        public NetworkButtonPresenter(NetworkButton networkButton)
        {
            _networkButton = networkButton;

            AppKit.NetworkController.ChainChanged += ChainChangedHandler;
            UpdateNetworkButton(AppKit.NetworkController.ActiveChain);
        }

        private void ChainChangedHandler(object sender, NetworkController.ChainChangedEventArgs e)
        {
            UpdateNetworkButton(e.NewChain);
        }

        private void UpdateNetworkButton(Chain chain)
        {
            if (chain == null)
            {
                _networkButton.NetworkName.text = "Network";
                _networkButton.NetworkIcon.style.display = DisplayStyle.None;
                return;
            }

            _networkButton.NetworkName.text = chain.Name;

            var newNetworkIcon = RemoteSpriteFactory.GetRemoteSprite<Image>(chain.ImageUrl);
            _networkIcon?.UnsubscribeImage(_networkButton.NetworkIcon);
            _networkIcon = newNetworkIcon;
            _networkIcon.SubscribeImage(_networkButton.NetworkIcon);
            _networkButton.NetworkIcon.style.display = DisplayStyle.Flex;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            AppKit.NetworkController.ChainChanged -= ChainChangedHandler;
            _networkIcon?.UnsubscribeImage(_networkButton.NetworkIcon);

            _disposed = true;
        }
    }
}