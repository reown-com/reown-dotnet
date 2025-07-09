using UnityEngine.UIElements;

namespace Reown.AppKit.Unity.Test
{
    public class TestView
    {
        public VisualElement Root { get; private set; }

        public TestView(VisualElement root)
        {
            Root = root;
        }

        public T Q<T>(string name = null) where T : VisualElement
        {
            return Root.Q<T>(name);
        }
    }
}