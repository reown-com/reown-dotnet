using Reown.AppKit.Unity.Views.SiweView;
using UnityEngine.UIElements;

namespace Reown.AppKit.Unity
{
    public class SiwePresenter : Presenter<SiweView>
    {
        private bool _success;

        public SiwePresenter(RouterController router, VisualElement parent, bool hideView = true) : base(router, parent, hideView)
        {
            AppKit.SiweController.Config.SignInSuccess += OnSignInSuccess;
            AppKit.SiweController.Config.SignOutSuccess += OnSignOutSuccess;
        }

        private void OnSignInSuccess(SiweSession siweSession)
        {
            _success = true;
        }

        private void OnSignOutSuccess()
        {
            Router.GoBack();
        }

        protected override async void OnHideCore()
        {
            base.OnHideCore();

            if (!_success)
            {
                await AppKit.ConnectorController.DisconnectAsync();
            }
        }

        public async void OnApproveButtonClick()
        {
            // TODO: send personal sign request
        }

        public async void OnRejectButtonClick()
        {
        }

        // TODO: unsubscribe on dispose
    }
}