using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reown.AppKit.Unity.Components
{
    public class Snackbar : VisualElement
    {
        public static readonly string ClassName = "snackbar";
        public static readonly string ClassNameIconContainer = $"{ClassName}__icon-container";
        public static readonly string ClassNameIconContainerColorError = $"{ClassNameIconContainer}--color-error";
        public static readonly string ClassNameIconContainerColorSuccess = $"{ClassNameIconContainer}--color-success";
        public static readonly string ClassNameIcon = $"{ClassName}__icon";
        public static readonly string ClassNameMessage = $"{ClassName}__message";

        public IconColor CurrentIconColor
        {
            get => _currentIconColor;
            set
            {
                if (!string.IsNullOrWhiteSpace(_currentColorClass)) _iconContainer.RemoveFromClassList(_currentColorClass);

                switch (value)
                {
                    case IconColor.Error:
                        _iconContainer.AddToClassList(ClassNameIconContainerColorError);
                        _currentColorClass = ClassNameIconContainerColorError;
                        break;
                    case IconColor.Success:
                        _iconContainer.AddToClassList(ClassNameIconContainerColorSuccess);
                        _currentColorClass = ClassNameIconContainerColorSuccess;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(value), value, null);
                }

                _currentIconColor = value;
            }
        }

        public string Message
        {
            get => _message.text;
            set => _message.text = value;
        }

        public Image Icon { get; }

        private readonly VisualElement _iconContainer;
        private readonly Label _message;

        private IconColor _currentIconColor;
        private string _currentColorClass;

        public new class UxmlFactory : UxmlFactory<Snackbar>
        {
        }

        public Snackbar() : this(IconColor.Success, "Hello, friend!")
        {
        }

        public Snackbar(IconColor iconColor, string message)
        {
            var asset = Resources.Load<VisualTreeAsset>("Reown/AppKit/Components/Snackbar/Snackbar");
            asset.CloneTree(this);

            AddToClassList(ClassName);

            _iconContainer = this.Q<VisualElement>(ClassNameIconContainer);
            Icon = this.Q<Image>(ClassNameIcon);
            _message = this.Q<Label>(ClassNameMessage);

            CurrentIconColor = iconColor;
            Message = message;
        }

        public enum IconColor
        {
            Error,
            Success
        }
    }
}