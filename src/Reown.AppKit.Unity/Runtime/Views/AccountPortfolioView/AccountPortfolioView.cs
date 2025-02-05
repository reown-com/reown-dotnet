using System;
using Reown.AppKit.Unity.Utils;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reown.AppKit.Unity.Components
{
    public class AccountPortfolioView : VisualElement
    {
        public const string Name = "account-portfolio-view";
        public static readonly string NameBalanceUsd = $"{Name}__balance";
        public static readonly string NameAccountChipContainer = $"{Name}__account-chip-container";
        public static readonly string NameAccountChip = $"{Name}__account-chip";
        public static readonly string NameAccountChipAvatarImage = $"{Name}__account-chip-avatar-image";
        public static readonly string NameAccountChipContentName = $"{Name}__account-chip-content-name";
        
        public Balance Balance { get; }

        public Image AvatarImage { get; }

        public Label AccountName { get; }

        public VisualElement AccountChipContainer { get; }

        public event Action AccountClicked
        {
            add
            {
                if (AccountChipClickable == null)
                    AccountChipClickable = new Clickable(value);
                else
                    AccountChipClickable.clicked += value;
            }
            remove
            {
                if (AccountChipClickable == null)
                    return;
                AccountChipClickable.clicked -= value;
            }
        }

        public Clickable AccountChipClickable
        {
            get => _accountChipClickable;
            set
            {
                _accountChipClickable = value;
                AccountChipContainer.AddManipulator(value);
            }
        }

        private Clickable _accountChipClickable;
        

        public new class UxmlFactory : UxmlFactory<AccountPortfolioView>
        {
        }

        public AccountPortfolioView() : this(null)
        {
        }

        public AccountPortfolioView(string visualTreePath)
        {
            var asset = Resources.Load<VisualTreeAsset>(visualTreePath ?? "Reown/AppKit/Views/AccountPortfolioView/AccountPortfolioView");
            asset.CloneTree(this);

            name = Name;

            Balance = this.Q<Balance>(NameBalanceUsd);
            AccountChipContainer = this.Q<VisualElement>(NameAccountChipContainer);
            AvatarImage = this.Q<Image>(NameAccountChipAvatarImage);
            AccountName = this.Q<Label>(NameAccountChipContentName);
        }

        public void SetProfileName(string value)
        {
            AccountName.text = value.FontWeight500();
        }
    }
}