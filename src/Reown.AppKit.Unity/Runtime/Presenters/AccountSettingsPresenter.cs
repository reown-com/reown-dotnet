using System;
using System.Collections.Generic;
using System.ComponentModel;
using Reown.AppKit.Unity.Components;
using Reown.AppKit.Unity.Profile;
using Reown.AppKit.Unity.Utils;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reown.AppKit.Unity
{
    public class AccountSettingsPresenter : Presenter<AccountSettingsView>
    {
        public override bool HeaderBorder
        {
            get => false;
        }

        // List of buttons at the bottom of the account view.
        // This list is used to enable/disable all buttons at once when needed.
        protected readonly HashSet<ListItem> Buttons = new();

        private bool _disposed;
        private ListItem _networkButton;
        private ListItem _smartAccountButton;
        private RemoteSprite<Image> _networkIcon;
        private RemoteSprite<Image> _avatar;

        private ProfileConnector _profileConnector;

        public AccountSettingsPresenter(RouterController router, VisualElement parent) : base(router, parent)
        {
            View.ExplorerButton.Clicked += OnBlockExplorerButtonClick;
            View.CopyLink.Clicked += OnCopyAddressButtonClick;
            
            AppKit.AccountController.PropertyChanged += AccountPropertyChangedHandler;
            AppKit.NetworkController.ChainChanged += ChainChangedHandler;
        }

        private void InitializeButtons(VisualElement buttonsListView)
        {
            CreateButtons(buttonsListView);
        }

        private void AccountPropertyChangedHandler(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(AccountController.ProfileName):
                    UpdateProfileName();
                    break;
                case nameof(AccountController.Address):
                case nameof(AccountController.ProfileAvatar):
                    UpdateProfileAvatar();
                    break;
                case nameof(AccountController.NativeTokenBalance):
                    View.SetBalance(TrimToThreeDecimalPlaces(AppKit.AccountController.NativeTokenBalance));
                    break;
                case nameof(AccountController.NativeTokenSymbol):
                    View.SetBalanceSymbol(AppKit.AccountController.NativeTokenSymbol);
                    break;
            }
        }

        private void ChainChangedHandler(object sender, NetworkController.ChainChangedEventArgs e)
        {
            UpdateNetworkButton(e.NewChain);
        }

        // Creates the buttons at the bottom of the account view.
        protected virtual void CreateButtons(VisualElement buttonsListView)
        {
            CreateNetworkButton(buttonsListView);
            CreateSmartAccountToggleButton(buttonsListView);
            CreateDisconnectButton(buttonsListView);
        }

        protected virtual void CreateSmartAccountToggleButton(VisualElement buttonsListView)
        {
            if (AppKit.ConnectorController.ActiveConnector is not ProfileConnector)
                return;

            _profileConnector = (ProfileConnector)AppKit.ConnectorController.ActiveConnector;

            var anotherAccountType = _profileConnector.PreferredAccountType == AccountType.SmartAccount
                ? AccountType.Eoa.ToFriendlyString()
                : AccountType.SmartAccount.ToFriendlyString();

            var icon = Resources.Load<VectorImage>("Reown/AppKit/Icons/icon_bold_swaphorizontal");
            _smartAccountButton = new ListItem($"Switch to your {anotherAccountType}", OnSmartAccountButtonClick, icon, ListItem.IconType.Circle, ListItem.IconStyle.Default);
            Buttons.Add(_smartAccountButton);
            buttonsListView.Add(_smartAccountButton);
        }

        protected virtual void CreateNetworkButton(VisualElement buttonsListView)
        {
            _networkButton = new ListItem("Network", OnNetworkButtonClick, null, ListItem.IconType.Circle)
            {
                IconFallbackElement =
                {
                    vectorImage = Resources.Load<VectorImage>("Reown/AppKit/Icons/icon_medium_info")
                }
            };
            Buttons.Add(_networkButton);
            buttonsListView.Add(_networkButton);
        }

        protected virtual void CreateDisconnectButton(VisualElement buttonsListView)
        {
            var disconnectIcon = Resources.Load<VectorImage>("Reown/AppKit/Icons/icon_medium_disconnect");
            var disconnectButton = new ListItem("Disconnect", OnDisconnectButtonClick, disconnectIcon, ListItem.IconType.Circle, ListItem.IconStyle.Default);
            Buttons.Add(disconnectButton);
            buttonsListView.Add(disconnectButton);
        }

        protected virtual void UpdateProfileName()
        {
            var profileName = AppKit.AccountController.ProfileName;
            if (profileName.Length > 15)
                profileName = profileName.Truncate(6);

            View.SetProfileName(profileName);
        }

        protected virtual void UpdateProfileAvatar()
        {
            var avatar = AppKit.AccountController.ProfileAvatar;

            if (avatar.IsEmpty || avatar.AvatarFormat != "png" && avatar.AvatarFormat != "jpg" && avatar.AvatarFormat != "jpeg")
            {
                var address = AppKit.AccountController.Address;
                var texture = UiUtils.GenerateAvatarTexture(address);
                View.ProfileAvatarImage.image = texture;
            }
            else
            {
                var remoteSprite = RemoteSpriteFactory.GetRemoteSprite<Image>(avatar.AvatarUrl);
                _avatar?.UnsubscribeImage(View.ProfileAvatarImage);
                _avatar = remoteSprite;
                _avatar.SubscribeImage(View.ProfileAvatarImage);
            }
        }

        protected override void OnVisibleCore()
        {
            base.OnVisibleCore();
            View.Buttons.Clear();
            InitializeButtons(View.Buttons);
            UpdateNetworkButton(AppKit.NetworkController.ActiveChain);
        }

        private void UpdateNetworkButton(Chain chain)
        {
            if (_networkButton == null)
                return;
            
            if (chain == null)
            {
                _networkButton.Label = "Network";
                _networkButton.IconImageElement.style.display = DisplayStyle.None;
                _networkButton.IconFallbackElement.style.display = DisplayStyle.Flex;
                _networkButton.ApplyIconStyle(ListItem.IconStyle.Error);
                return;
            }

            _networkButton.Label = chain.Name;

            var newNetworkIcon = RemoteSpriteFactory.GetRemoteSprite<Image>(chain.ImageUrl);

            _networkIcon?.UnsubscribeImage(_networkButton.IconImageElement);
            _networkIcon = newNetworkIcon;
            _networkIcon.SubscribeImage(_networkButton.IconImageElement);
            _networkButton.IconImageElement.style.display = DisplayStyle.Flex;
            _networkButton.IconFallbackElement.style.display = DisplayStyle.None;
            _networkButton.ApplyIconStyle(ListItem.IconStyle.None);
        }

        protected virtual async void OnDisconnectButtonClick()
        {
            try
            {
                ButtonsSetEnabled(false);
                await AppKit.DisconnectAsync();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                ButtonsSetEnabled(true);
            }
        }

        private void OnSmartAccountButtonClick()
        {
            var currentAccountType = _profileConnector.PreferredAccountType;
            var newAccountType = currentAccountType == AccountType.SmartAccount
                ? AccountType.Eoa
                : AccountType.SmartAccount;

            _profileConnector.SetPreferredAccount(newAccountType);

            _smartAccountButton.Label = $"Switch to your {currentAccountType.ToFriendlyString()}";

            AppKit.NotificationController.Notify(NotificationType.Success, $"Switched to {newAccountType.ToFriendlyString()}");

            AppKit.EventsController.SendEvent(new Event
            {
                name = "SET_PREFERRED_ACCOUNT_TYPE",
                properties = new Dictionary<string, object>
                {
                    { "network", AppKit.NetworkController.ActiveChain.Name.ToLowerInvariant() },
                    { "accountType", newAccountType.ToShortString() }
                }
            });
        }

        protected virtual void OnNetworkButtonClick()
        {
            Router.OpenView(ViewType.NetworkSearch);

            AppKit.EventsController.SendEvent(new Event
            {
                name = "CLICK_NETWORKS"
            });
        }

        protected virtual void OnBlockExplorerButtonClick()
        {
            var chain = AppKit.NetworkController.ActiveChain;
            var blockExplorerUrl = chain.BlockExplorer.url;
            var address = AppKit.AccountController.Address;
            Application.OpenURL($"{blockExplorerUrl}/address/{address}");
        }

        protected virtual void OnCopyAddressButtonClick()
        {
            var address = AppKit.AccountController.Address;
            GUIUtility.systemCopyBuffer = address;
            AppKit.NotificationController.Notify(NotificationType.Success, "Ethereum address copied");
        }

        private void ButtonsSetEnabled(bool value)
        {
            foreach (var button in Buttons)
                button.SetEnabled(value);
        }

        public static string TrimToThreeDecimalPlaces(float input)
        {
            return input.ToString("F3");
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                AppKit.AccountController.PropertyChanged -= AccountPropertyChangedHandler;
                AppKit.NetworkController.ChainChanged -= ChainChangedHandler;

                _networkIcon?.UnsubscribeImage(_networkButton.IconImageElement);
                _avatar?.UnsubscribeImage(View.ProfileAvatarImage);
            }

            _disposed = true;
            base.Dispose(disposing);
        }
    }
}