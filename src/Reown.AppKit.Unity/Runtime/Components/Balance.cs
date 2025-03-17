using UnityEngine;
using UnityEngine.UIElements;

namespace Reown.AppKit.Unity.Components
{
    public class Balance : VisualElement
    {
        public const string Name = "balance";
        public static readonly string NameSymbol = $"{Name}__symbol";
        public static readonly string NameInteger = $"{Name}__integer";
        public static readonly string NameDecimal = $"{Name}__decimal";

        public Label Symbol { get; }
        public Label Integer { get; }
        public Label Decimal { get; }

        public new class UxmlFactory : UxmlFactory<Balance>
        {
        }

        public Balance() : this(null)
        {
        }

        public Balance(string visualTreePath)
        {
            var asset = Resources.Load<VisualTreeAsset>(visualTreePath ?? "Reown/AppKit/Components/Balance/Balance");
            asset.CloneTree(this);

            name = Name;

            Symbol = this.Q<Label>(NameSymbol);
            Integer = this.Q<Label>(NameInteger);
            Decimal = this.Q<Label>(NameDecimal);
        }

        public void UpdateBalance(float balance, string symbol = "$")
        {
            Integer.text = balance.ToString("N0");
            Decimal.text = balance.ToString("F2")[balance.ToString("F0").Length..];
            Symbol.text = symbol;
        }
    }
}