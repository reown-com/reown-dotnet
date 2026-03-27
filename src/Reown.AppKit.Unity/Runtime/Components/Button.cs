using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reown.AppKit.Unity.Components
{
    [UxmlElement]
    public partial class Button : VisualElement
    {
        public const string Name = "button";

        public static readonly string ClassNameVariantMain = $"{Name}--variant-main";
        public static readonly string ClassNameVariantAccent = $"{Name}--variant-accent";
        public static readonly string ClassNameVariantShade = $"{Name}--variant-shade";

        public static readonly string ClassNameSizeSmall = $"{Name}--size-small";
        public static readonly string ClassNameSizeMedium = $"{Name}--size-medium";

        [UxmlAttribute]
        public ButtonVariant Variant
        {
            get => _variant;
            set
            {
                _variant = value;
                switch (value)
                {
                    case ButtonVariant.Main:
                        AddToClassList(ClassNameVariantMain);
                        break;
                    case ButtonVariant.Accent:
                        AddToClassList(ClassNameVariantAccent);
                        break;
                    case ButtonVariant.Shade:
                        AddToClassList(ClassNameVariantShade);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(value), value, null);
                }
            }
        }

        [UxmlAttribute]
        public ButtonSize Size
        {
            get => _size;
            set
            {
                _size = value;
                switch (value)
                {
                    case ButtonSize.Small:
                        AddToClassList(ClassNameSizeSmall);
                        break;
                    case ButtonSize.Medium:
                        AddToClassList(ClassNameSizeMedium);
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

        private ButtonVariant _variant;
        private ButtonSize _size;
        private Clickable _clickable;

        public Button()
        {
            var asset = Resources.Load<VisualTreeAsset>("Reown/AppKit/Components/Button/Button");
            asset.CloneTree(this);

            name = Name;
        }
    }

    public enum ButtonVariant
    {
        Main,
        Accent,
        Shade
    }

    public enum ButtonSize
    {
        Small,
        Medium
    }
}