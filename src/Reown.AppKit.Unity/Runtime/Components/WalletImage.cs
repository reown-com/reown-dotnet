using UnityEngine.UIElements;
using Reown.AppKit.Unity.Utils;

namespace Reown.AppKit.Unity.Components
{
    [UxmlElement]
    public partial class WalletImage : VisualElement
    {
        public const string Name = "wallet-image";
        public static readonly string ClassNameSmall = $"{Name}--size-small";
        public static readonly string ClassNameMedium = $"{Name}--size-medium";
        public static readonly string ClassNameLarge = $"{Name}--size-large";

        private VisualElementSize _size;

        public WalletImage()
        {
            AddToClassList(Name);
        }

        [UxmlAttribute]
        public VisualElementSize Size
        {
            get => _size;
            set
            {
                _size = value;
                switch (value)
                {
                    case VisualElementSize.Small:
                        AddToClassList(ClassNameSmall);
                        break;
                    case VisualElementSize.Medium:
                        AddToClassList(ClassNameMedium);
                        break;
                    case VisualElementSize.Large:
                        AddToClassList(ClassNameLarge);
                        break;
                }
            }
        }
    }
}