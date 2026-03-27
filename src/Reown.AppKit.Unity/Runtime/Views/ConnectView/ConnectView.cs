using UnityEngine;
using UnityEngine.UIElements;

namespace Reown.AppKit.Unity.Views.ConnectView
{
    [UxmlElement]
    public partial class ConnectView : ScrollView
    {
        public const string Name = "connect-view";

        public ConnectView()
        {
            var uss = Resources.Load<StyleSheet>("Reown/AppKit/Views/ConnectView/ConnectView");
            styleSheets.Add(uss);

            verticalScrollerVisibility = ScrollerVisibility.Hidden;

            name = Name;
        }
    }
}