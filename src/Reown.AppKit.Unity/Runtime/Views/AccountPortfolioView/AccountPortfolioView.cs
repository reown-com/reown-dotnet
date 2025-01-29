using UnityEngine;
using UnityEngine.UIElements;

namespace Reown.AppKit.Unity.Components
{
    public class AccountPortfolioView : VisualElement
    {
        public const string Name = "account-portfolio-view";
        public static readonly string NameBalanceUsd = $"{Name}__balance";

        public Balance Balance { get; }

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
        }
    }
}