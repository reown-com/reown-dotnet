using Reown.AppKit.Unity;
using Reown.AppKit.Unity.Components;
using UnityEngine;
using UnityEngine.UIElements;

namespace WalletConnect.Web3Modal.CustomizationSample
{
    public class CustomAccountPresenter : AccountPresenter
    {
        public CustomAccountPresenter(RouterController router, VisualElement parent) : base(router, parent)
        {
        }

        protected override AccountView CreateViewInstance()
        {
            return new AccountView("UI/CustomAccountView");
        }

        protected override void CreateButtons(VisualElement buttonsListView)
        {
            CreateOpenSeaButton(buttonsListView);

            base.CreateButtons(buttonsListView);
        }

        private void CreateOpenSeaButton(VisualElement buttonsListView)
        {
            var openSeaIcon = Resources.Load<Sprite>("OpenSea Logo");
            var openSeaButton = new ListItem(
                "OpenSea",
                openSeaIcon,
                OnOpenSeaButtonClick,
                iconType: ListItem.IconType.Circle,
                iconStyle: ListItem.IconStyle.Accent);

            Buttons.Add(openSeaButton);
            buttonsListView.Add(openSeaButton);
        }

        private void OnOpenSeaButtonClick()
        {
            var address = AppKit.AccountController.Address;
            var url = $"https://opensea.io/{address}";
            Application.OpenURL(url);
        }
    }
}