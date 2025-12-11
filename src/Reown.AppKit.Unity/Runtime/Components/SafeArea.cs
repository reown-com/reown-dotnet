using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reown.AppKit.Unity.Components
{
    /// <summary>
    /// SafeArea Container for UI Toolkit.
    /// </summary>
    [UxmlElement]
    public partial class SafeArea : VisualElement
    {
        private struct Offset
        {
            public float Left, Right, Top, Bottom;

            public override string ToString()
            {
                return $"l: {Left}, r: {Right}, t:{Top}, b: {Bottom}";
            }
        }

        [UxmlAttribute]
        public bool CollapseMargins { get; set; }
        [UxmlAttribute]
        public bool ExcludeLeft { get; set; }
        [UxmlAttribute]
        public bool ExcludeRight { get; set; }
        [UxmlAttribute]
        public bool ExcludeTop { get; set; }
        [UxmlAttribute]
        public bool ExcludeBottom { get; set; }
        [UxmlAttribute]
        public bool ExcludeTvos { get; set; }

        private VisualElement _contentContainer;
        public override VisualElement contentContainer => _contentContainer;

        public SafeArea()
        {
            // By using absolute position instead of flex to fill the full screen, SafeArea containers can be stacked.
            style.position = Position.Absolute;
            style.top = 0;
            style.bottom = 0;
            style.left = 0;
            style.right = 0;
            pickingMode = PickingMode.Ignore;

            _contentContainer = new VisualElement();
            _contentContainer.name = "safe-area-content-container";
            _contentContainer.pickingMode = PickingMode.Ignore;
            _contentContainer.style.flexGrow = 1;
            _contentContainer.style.flexShrink = 0;
            hierarchy.Add(_contentContainer);

            RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            // As RuntimePanelUtils is not available in UIBuilder,
            // the handling is wrapped in a try/catch to avoid InvalidCastExceptions when working in UIBuilder.
            try
            {
                var safeArea = GetSafeAreaOffset();
                var margin = GetMarginOffset();

                if (CollapseMargins)
                {
                    _contentContainer.style.marginLeft = Mathf.Max(margin.Left, safeArea.Left) - margin.Left;
                    _contentContainer.style.marginRight = Mathf.Max(margin.Right, safeArea.Right) - margin.Right;
                    _contentContainer.style.marginTop = Mathf.Max(margin.Top, safeArea.Top) - margin.Top;
                    _contentContainer.style.marginBottom = Mathf.Max(margin.Bottom, safeArea.Bottom) - margin.Bottom;
                }
                else
                {
                    _contentContainer.style.marginLeft = safeArea.Left;
                    _contentContainer.style.marginRight = safeArea.Right;
                    _contentContainer.style.marginTop = safeArea.Top;
                    _contentContainer.style.marginBottom = safeArea.Bottom;
                }
            }
            catch (System.InvalidCastException)
            {
            }
        }

        // Convert screen safe area to panel space and get the offset values from the panel edges.
        private Offset GetSafeAreaOffset()
        {
            var safeArea = Screen.safeArea;
            var leftTop = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(safeArea.xMin, Screen.height - safeArea.yMax));
            var rightBottom = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(Screen.width - safeArea.xMax, safeArea.yMin));

#if UNITY_TVOS
            if (ExcludeTvos)
                return new Offset { Left = 0, Right = 0, Top = 0, Bottom = 0 };
#endif

            // If the user has flagged an edge as excluded, set that edge to 0.
            return new Offset
            {
                Left = ExcludeLeft ? 0 : leftTop.x,
                Right = ExcludeRight ? 0 : rightBottom.x,
                Top = ExcludeTop ? 0 : leftTop.y,
                Bottom = ExcludeBottom ? 0 : rightBottom.y
            };
        }

        // Get the resolved margins from the inline style.
        private Offset GetMarginOffset()
        {
            return new Offset()
            {
                Left = resolvedStyle.marginLeft,
                Right = resolvedStyle.marginRight,
                Top = resolvedStyle.marginTop,
                Bottom = resolvedStyle.marginBottom
            };
        }
    }
}