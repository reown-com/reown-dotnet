using UnityEngine;
using UnityEngine.UIElements;

namespace Reown.AppKit.Unity.Components
{
    public class SocialLoginButtons : VisualElement
    {
        public const string Name = "social-login-buttons";

        private VisualElement _smallButtonsContainer;

        public new class UxmlFactory : UxmlFactory<SocialLoginButtons>
        {
        }

        public SocialLoginButtons()
        {
        }

        public SocialLoginButtons(SocialLogin[] loginProviders)
        {
            if (loginProviders.Length == 0)
                return;

            name = Name;

            if (loginProviders.Length == 2)
            {
                // For exactly 2 providers, show both as small buttons
                for (var i = 0; i < loginProviders.Length; i++)
                    AddSmallButton(loginProviders[i], i, 2);
            }
            else if (loginProviders.Length > 0)
            {
                // For 1 or 3+ providers, show first as big button, rest as small
                AddBigButton(loginProviders[0]);

                var smallButtonsCount = loginProviders.Length - 1;
                for (var i = 1; i < loginProviders.Length; i++)
                    AddSmallButton(loginProviders[i], i - 1, smallButtonsCount);
            }
        }

        private void AddBigButton(SocialLogin provider)
        {
            // Create a container for the big button
            var container = new VisualElement
            {
                name = $"{Name}__big-button-container",
                style =
                {
                    flexGrow = 1
                }
            };

            // Create a big button
            var icon = LoadSocialProviderIcon(provider);
            var button = new ListItem(
                $"Continue with {provider.Name}",
                icon,
                provider.Open,
                iconType: ListItem.IconType.Circle
            );

            // Center the label
            var label = button.Q<Label>("list-item__label");
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.paddingRight = 35;

            container.Add(button);
            Add(container);
        }

        private void AddSmallButton(SocialLogin provider, int index, int total)
        {
            if (_smallButtonsContainer == null)
            {
                _smallButtonsContainer = new VisualElement
                {
                    name = $"{Name}__small-buttons-container",
                    style =
                    {
                        flexDirection = FlexDirection.Row,
                        alignItems = Align.Center,
                        justifyContent = Justify.SpaceBetween
                    }
                };

                Add(_smallButtonsContainer);
            }

            var icon = LoadSocialProviderIcon(provider);
            var button = new ListItem(icon, provider.Open, ListItem.IconType.Circle);

            // Add right padding to create spacing between buttons
            if (index < total - 1)
                button.style.marginRight = 8;

            _smallButtonsContainer.Add(button);
        }

        protected virtual VectorImage LoadSocialProviderIcon(SocialLogin provider)
        {
            return Resources.Load<VectorImage>($"Reown/AppKit/Images/Social/{provider.Slug}");
        }
    }
}