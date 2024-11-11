using UnityEngine;
using UnityEngine.UIElements;
using Button = Reown.AppKit.Unity.Components.Button;

namespace Reown.AppKit.Unity.Views.SiweView
{
    public class SiweView : VisualElement
    {
        public const string Name = "siwe-view";
        public static readonly string NameLogoApp = $"{Name}__logo-app";
        public static readonly string NameLogoWallet = $"{Name}__logo-wallet";
        public static readonly string NameTitle = $"{Name}__title";
        public static readonly string NameCancelButton = $"{Name}__cancel-button";
        public static readonly string NameApproveButton = $"{Name}__approve-button";

        public static readonly string ClassNameLogoMoveLeft = $"{Name}__logo-move-left";
        public static readonly string ClassNameLogoMoveRight = $"{Name}__logo-move-right";

        public string Title
        {
            get => _titleLabel.text;
            set => _titleLabel.text = value;
        }

        private readonly Image _logoApp;
        private readonly Image _logoWallet;
        private readonly Label _titleLabel;

        public new class UxmlFactory : UxmlFactory<SiweView>
        {
        }

        public SiweView() : this(null)
        {
        }

        public SiweView(string visualTreePath)
        {
            var asset = Resources.Load<VisualTreeAsset>(visualTreePath ?? "Reown/AppKit/Views/SiweView/SiweView");
            asset.CloneTree(this);

            name = Name;

            _logoApp = this.Q<Image>(NameLogoApp);
            _logoWallet = this.Q<Image>(NameLogoWallet);
            _titleLabel = this.Q<Label>(NameTitle);

            // App and wallet logo animation
            _logoApp.RegisterCallback<TransitionEndEvent, VisualElement>((_, x) => x.ToggleInClassList(ClassNameLogoMoveLeft), _logoApp);
            _logoWallet.RegisterCallback<TransitionEndEvent, VisualElement>((_, x) => x.ToggleInClassList(ClassNameLogoMoveRight), _logoWallet);
            schedule.Execute(() =>
            {
                _logoApp.ToggleInClassList(ClassNameLogoMoveLeft);
                _logoWallet.ToggleInClassList(ClassNameLogoMoveRight);
            }).StartingIn(100);

            var approveButton = this.Q<Button>();
            approveButton.Clicked += () => Debug.Log("Approve button clicked");

        }
    }
}