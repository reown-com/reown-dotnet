using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reown.AppKit.Unity.Components
{
    [UxmlElement]
    public partial class Link : VisualElement
    {
        public const string Name = "link";
        public static readonly string NameIcon = $"{Name}__icon";
        public static readonly string ClassNameSizeSmall = $"{Name}--size-small";
        public static readonly string ClassNameSizeMedium = $"{Name}--size-medium";
        public static readonly string ClassNameVariantMain = $"{Name}--variant-main";
        public static readonly string ClassNameVariantGray = $"{Name}--variant-gray";
        public static readonly string ClassNameVariantIcon = $"{Name}--variant-icon";

        [UxmlAttribute]
        public string Text
        {
            get => _label.text ?? string.Empty;
            set => _label.text = value;
        }

        [UxmlAttribute]
        public LinkSize Size
        {
            get => _size;
            set
            {
                _size = value;
                switch (value)
                {
                    case LinkSize.Small:
                        AddToClassList(ClassNameSizeSmall);
                        break;
                    case LinkSize.Medium:
                        AddToClassList(ClassNameSizeMedium);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(value), value, null);
                }
            }
        }

        [UxmlAttribute]
        public string Icon
        {
            set
            {
                if (!string.IsNullOrEmpty(value))
                    icon.vectorImage = Resources.Load<VectorImage>(value);
            }
        }

        [UxmlAttribute]
        public LinkVariant Variant
        {
            get => _variant;
            set
            {
                _variant = value;
                switch (value)
                {
                    case LinkVariant.Main:
                        AddToClassList(ClassNameVariantMain);
                        break;
                    case LinkVariant.Gray:
                        AddToClassList(ClassNameVariantGray);
                        break;
                    case LinkVariant.Icon:
                        AddToClassList(ClassNameVariantIcon);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(value), value, null);
                }
            }
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

        public readonly Image icon;
        private readonly Label _label;
        private LinkSize _size = LinkSize.Small;
        private LinkVariant _variant = LinkVariant.Main;
        private Clickable _clickable;

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

        public Link()
        {
            var asset = Resources.Load<VisualTreeAsset>("Reown/AppKit/Components/Link/Link");
            asset.CloneTree(this);

            name = Name;

            icon = this.Q<Image>(NameIcon);
            _label = this.Q<Label>();
            focusable = true;
        }

        public Link(string text, Action clickEvent) : this()
        {
            Text = text;
            Clickable = new Clickable(clickEvent);
        }
    }

    public enum LinkVariant
    {
        Main,
        Gray,
        Icon
    }

    public enum LinkSize
    {
        Small,
        Medium
    }
}