using Reown.AppKit.Unity.Views.SiweView;
using UnityEngine.UIElements;

namespace Reown.AppKit.Unity
{
    public class SiwePresenter : Presenter<SiweView>
    {
        public override string Title
        {
            get => "Sign In";
        }

        private bool _success;

        public SiwePresenter(RouterController router, VisualElement parent, bool hideView = true) : base(router, parent, hideView)
        {
            AppKit.SiweController.Config.SignInSuccess += SignInSuccessHandler;
            AppKit.SiweController.Config.SignOutSuccess += SignOutSuccessHandler;

            AppKit.ConnectorController.SignatureRequested += SignatureRequestedHandler;
        }

        private void SignInSuccessHandler(SiweSession siweSession)
        {
            _success = true;
        }

        private void SignOutSuccessHandler()
        {
            Router.GoBack();
        }

        private void SignatureRequestedHandler(object sender, SignatureRequest e)
        {
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