using UnityEngine;
using UnityEngine.UIElements;

namespace Reown.AppKit.Unity.Views.SiweView
{
    public class SiweView : VisualElement
    {
        public const string Name = "siwe-view";
        public static readonly string NameApproveButton = $"{Name}__approve-button";

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

            var approveButton = this.Q<Button>(NameApproveButton);
            approveButton.clicked += () => Debug.Log("Approve button clicked");
        }
    }
}