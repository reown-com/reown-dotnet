using System.ComponentModel;
using Reown.AppKit.Unity.Components;
using UnityEngine.UIElements;

namespace Reown.AppKit.Unity
{
    public class AccountPortfolioPresenter : Presenter<AccountPortfolioView>
    {
        public AccountPortfolioPresenter(RouterController router, VisualElement parent) : base(router, parent)
        {
            AppKit.AccountController.PropertyChanged += AccountPropertyChangedHandler;
        }

        private void AccountPropertyChangedHandler(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(AccountController.TotalBalanceUsd):
                    View.Balance.UpdateBalance(AppKit.AccountController.TotalBalanceUsd);
                    break;
            }
        }
    }
}