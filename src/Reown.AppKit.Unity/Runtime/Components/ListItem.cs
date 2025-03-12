using System;
using Reown.AppKit.Unity.Utils;
using UnityEngine;
using UnityEngine.UIElements;
using Reown.AppKit.Unity.Utils;

namespace Reown.AppKit.Unity.Components
{
    public class ListItem : VisualElement
    {
        public const string Name = "list-item";
        public static readonly string NameLabel = $"{Name}__label";
        public static readonly string NameIconImage = $"{Name}__icon-image";
        public static readonly string NameIconFallback = $"{Name}__icon-fallback";
        public static readonly string NameStatusIconContainer = $"{Name}__status-icon-container";
        public static readonly string NameRightSlot = $"{Name}__right-slot";

        public static readonly string ClassIconVariantSquare = $"{Name}--icon-variant-square";
        public static readonly string ClassIconVariantCircle = $"{Name}--icon-variant-circle";
        public static readonly string ClassIconStyleAccent = $"{Name}--icon-style-accent";
        public static readonly string ClassIconStyleError = $"{Name}--icon-style-error";
        public static readonly string ClassIconStyleDefault = $"{Name}--icon-style-default";
        public static readonly string ClassCenteredIcon = $"{Name}--centered-icon";

        public new class UxmlFactory : UxmlFactory<ListItem>
        {
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

        public string Label
        {
            get => LabelElement.text;
            set => LabelElement.text = value.FontWeight500();
        }

        public Label LabelElement { get; private set; }

        public Image IconImageElement { get; private set; }

        public Image IconFallbackElement { get; private set; }

        public VisualElement IconContainerElement { get; private set; }

        public VisualElement RightSlot { get; private set; }

        public event Action Clicked
        {
            add
            {
                if (Clickable == null)
                    Clickable = new Clickable(value);
                else
                    Clickable.clicked += value;
            }
            remove
            {
                if (Clickable == null)
                    return;
                Clickable.clicked -= value;
            }
        }

        private Clickable _clickable;
        private string _iconStyleClass = string.Empty;

        public ListItem() : this("WalletConnect", (Sprite)null, null)
        {
        }

        public ListItem(
            string label,
            Sprite icon,
            Action clickEvent,
            VectorImage fallbackIcon = null,
            IconType iconType = IconType.Square,
            IconStyle iconStyle = IconStyle.None,
            StatusIconType statusIconType = StatusIconType.None)
        {
            Build(label, clickEvent, fallbackIcon, iconType, iconStyle, statusIconType);

            if (icon != null)
            {
                IconImageElement.sprite = icon;
                IconFallbackElement.style.display = DisplayStyle.None;
            }
        }

        public ListItem(
            string label,
            Action clickEvent,
            VectorImage fallbackIcon = null,
            IconType iconType = IconType.Square,
            IconStyle iconStyle = IconStyle.None,
            StatusIconType statusIconType = StatusIconType.None)
        {
            Build(label, clickEvent, fallbackIcon, iconType, iconStyle, statusIconType);
            IconImageElement.style.display = DisplayStyle.None;
        }

        public ListItem(
            string label,
            RemoteSprite<Image> icon,
            Action clickEvent,
            VectorImage fallbackIcon = null,
            IconType iconType = IconType.Square,
            IconStyle iconStyle = IconStyle.None,
            StatusIconType statusIconType = StatusIconType.None)
        {
            Build(label, clickEvent, fallbackIcon, iconType, iconStyle, statusIconType);

            if (icon != null)
            {
                IconFallbackElement.style.display = DisplayStyle.None;

                icon.SubscribeImage(IconImageElement);

                RegisterCallback<DetachFromPanelEvent, RemoteSprite<Image>>(
                    (_, remoteSprite) => remoteSprite.UnsubscribeImage(IconImageElement), icon
                );
            }
        }

        public ListItem(
            string label,
            VectorImage icon,
            Action clickEvent,
            VectorImage fallbackIcon = null,
            IconType iconType = IconType.Square,
            IconStyle iconStyle = IconStyle.None,
            StatusIconType statusIconType = StatusIconType.None)
        {
            Build(label, clickEvent, null, iconType, iconStyle, statusIconType);

            if (icon != null)
            {
                IconImageElement.vectorImage = icon;
                IconFallbackElement.style.display = DisplayStyle.None;
            }
        }

        public ListItem(
            VectorImage icon,
            Action clickEvent = null,
            IconType iconType = IconType.Square,
            IconStyle iconStyle = IconStyle.None,
            StatusIconType statusIconType = StatusIconType.None)
        {
            Build(null, clickEvent, null, iconType, iconStyle, statusIconType);
            if (icon != null)
            {
                IconImageElement.vectorImage = icon;
                IconFallbackElement.style.display = DisplayStyle.None;
            }

            CenterIcon();
        }

        private void CenterIcon()
        {
            if (LabelElement != null)
                LabelElement.style.display = DisplayStyle.None;

            if (RightSlot != null)
                RightSlot.style.display = DisplayStyle.None;

            AddToClassList(ClassCenteredIcon);
        }

        private void Build(
            string label,
            Action clickEvent,
            VectorImage fallbackIcon = null,
            IconType iconType = IconType.Square,
            IconStyle iconStyle = IconStyle.None,
            StatusIconType statusIconType = StatusIconType.None)
        {
            var asset = Resources.Load<VisualTreeAsset>("Reown/AppKit/Components/ListItem/ListItem");
            asset.CloneTree(this);

            name = Name;

            LabelElement = this.Q<Label>(NameLabel);
            IconImageElement = this.Q<Image>(NameIconImage);
            IconFallbackElement = this.Q<Image>(NameIconFallback);
            IconContainerElement = this.Q<VisualElement>(NameStatusIconContainer);
            RightSlot = this.Q<VisualElement>(NameRightSlot);

            Clickable = new Clickable(clickEvent);
            focusable = true;

            if (!string.IsNullOrEmpty(label))
            {
                Label = label;
            }

            if (fallbackIcon != null)
                IconFallbackElement.vectorImage = fallbackIcon;

            var iconContainer = IconContainerElement;

            ApplyIconType(iconType);
            ApplyIconStyle(iconStyle);
            ApplyStatusIconType(statusIconType, iconContainer);
        }

        private void ApplyIconType(IconType iconType)
        {
            switch (iconType)
            {
                case IconType.Square:
                    AddToClassList(ClassIconVariantSquare);
                    break;
                case IconType.Circle:
                    AddToClassList(ClassIconVariantCircle);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(iconType), iconType, null);
            }
        }

        public void ApplyIconStyle(IconStyle iconStyle)
        {
            if (!string.IsNullOrWhiteSpace(_iconStyleClass))
            {
                RemoveFromClassList(_iconStyleClass);
                _iconStyleClass = string.Empty;
            }

            switch (iconStyle)
            {
                case IconStyle.None:
                    break;
                case IconStyle.Default:
                    _iconStyleClass = ClassIconStyleDefault;
                    break;
                case IconStyle.Accent:
                    _iconStyleClass = ClassIconStyleAccent;
                    break;
                case IconStyle.Error:
                    _iconStyleClass = ClassIconStyleError;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(iconStyle), iconStyle, null);
            }

            if (!string.IsNullOrWhiteSpace(_iconStyleClass))
                AddToClassList(_iconStyleClass);
        }

        private static void ApplyStatusIconType(StatusIconType statusIconType, VisualElement iconContainer)
        {
            switch (statusIconType)
            {
                case StatusIconType.None:
                    iconContainer.style.display = DisplayStyle.None;
                    break;
                case StatusIconType.Success:
                    iconContainer.Add(new StatusIcon(StatusIcon.IconType.Success, StatusIcon.IconColor.Success));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(statusIconType), statusIconType, null);
            }
        }

        public enum IconType
        {
            Square,
            Circle
        }

        public enum IconStyle
        {
            None,
            Default,
            Accent,
            Error
        }
    }
}