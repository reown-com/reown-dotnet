using UnityEngine;
using UnityEngine.UIElements;

namespace Reown.AppKit.Unity.Views.WebWalletView
{
    public class SocialLoginView : VisualElement
    {
        public const string Name = "social-login-view";
        public readonly string NameProviderIcon = $"{Name}__provider-icon";
        public readonly string NameMainLabel = $"{Name}__main-label";
        public readonly string NameMessageLabel = $"{Name}__message";

        public readonly Image ProviderIcon;
        public readonly Label MainLabel;
        public readonly Label MessageLabel;

        public new class UxmlFactory : UxmlFactory<SocialLoginView>
        {
        }

        public SocialLoginView() : this(null)
        {
        }

        public SocialLoginView(string visualTreePath)
        {
            var asset = Resources.Load<VisualTreeAsset>(visualTreePath ?? "Reown/AppKit/Views/SocialLoginView/SocialLoginView");
            asset.CloneTree(this);

            name = Name;

            ProviderIcon = this.Q<Image>(NameProviderIcon);
            MainLabel = this.Q<Label>(NameMainLabel);
            MessageLabel = this.Q<Label>(NameMessageLabel);
        }
    }
}