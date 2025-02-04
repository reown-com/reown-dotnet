using System.ComponentModel;
using Reown.AppKit.Unity.Components;
using Reown.AppKit.Unity.Utils;
using UnityEngine.UIElements;

namespace Reown.AppKit.Unity
{
    public class AccountPortfolioPresenter : Presenter<AccountPortfolioView>
    {
        public override bool HeaderBorder
        {
            get => false;
        }

        private bool _disposed;
        private RemoteSprite<Image> _avatar;
        
        public AccountPortfolioPresenter(RouterController router, VisualElement parent) : base(router, parent)
        {
            AppKit.AccountController.PropertyChanged += AccountPropertyChangedHandler;
            View.AccountClicked += AccountClickedHandler;
        }

        private void AccountClickedHandler()
        {
            Router.OpenView(ViewType.AccountSettings);
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
                case nameof(AccountController.TotalBalanceUsd):
                    View.Balance.UpdateBalance(AppKit.AccountController.TotalBalanceUsd);
                    break;
            }
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
                View.AvatarImage.image = texture;
            }
            else
            {
                var remoteSprite = RemoteSpriteFactory.GetRemoteSprite<Image>(avatar.AvatarUrl);
                _avatar?.UnsubscribeImage(View.AvatarImage);
                _avatar = remoteSprite;
                _avatar.SubscribeImage(View.AvatarImage);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                AppKit.AccountController.PropertyChanged -= AccountPropertyChangedHandler;

                _avatar?.UnsubscribeImage(View.AvatarImage);
            }

            _disposed = true;
            base.Dispose(disposing);
        }
    }
}