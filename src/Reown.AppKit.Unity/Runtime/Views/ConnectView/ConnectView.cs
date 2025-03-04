using UnityEngine;
using UnityEngine.UIElements;

namespace Reown.AppKit.Unity.Views.ConnectView
{
    public class ConnectView : ScrollView
    {
        public const string Name = "connect-view";

        public new class UxmlFactory : UxmlFactory<ConnectView>
        {
        }

        public ConnectView()
        {
            var uss = Resources.Load<StyleSheet>("Reown/AppKit/Views/ConnectView/ConnectView");
            styleSheets.Add(uss);

            verticalScrollerVisibility = ScrollerVisibility.Hidden;

            name = Name;
        }
    }
}