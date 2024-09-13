using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reown.AppKit.Unity
{
    public abstract class PresenterBase : IDisposable
    {
        public virtual string Title { get; protected set; } = string.Empty;

        public virtual bool HeaderBorder { get; protected set; } = true;

        public RouterController Router { get; protected set; }

        public virtual VisualElement ViewVisualElement { get; protected set; }

        public bool IsVisible { get; private set; }

        private bool _disposed;

        public void OnVisible()
        {
            IsVisible = true;
            OnVisibleCore();
        }

        public void OnHide()
        {
            IsVisible = false;
            OnHideCore();
        }

        public void OnDisable()
        {
            IsVisible = false;
            OnDisableCore();
        }


        public void ShowViewVisualElement()
        {
            ViewVisualElement.style.display = DisplayStyle.Flex;
        }

        public void HideViewVisualElement()
        {
            ViewVisualElement.style.display = DisplayStyle.None;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                IsVisible = false;
            }

            _disposed = true;
        }

        protected abstract void OnVisibleCore();

        protected abstract void OnHideCore();

        protected abstract void OnDisableCore();
    }

    public class Presenter<TView> : PresenterBase where TView : VisualElement, new()
    {
        protected TView View { get; set; }

        protected VisualElement Parent { get; }

        private bool _disposed;

        public override VisualElement ViewVisualElement
        {
            get => View;
        }

        public Presenter(RouterController router, VisualElement parent, bool hideView = true)
        {
            Router = router;
            Parent = parent;
            BuildView(hideView);
        }

        protected void BuildView(bool hideView)
        {
            View = CreateViewInstance();
            View.style.display = hideView ? DisplayStyle.None : DisplayStyle.Flex;
            Parent.Add(View);
        }

        protected virtual TView CreateViewInstance()
        {
            return new TView();
        }

        protected override void OnVisibleCore()
        {
        }

        protected override void OnHideCore()
        {
        }

        protected override void OnDisableCore()
        {
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                if (View != null)
                    Parent.Remove(View);
            }

            _disposed = true;
            base.Dispose(disposing);
        }
    }
}