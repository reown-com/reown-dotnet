using System;
using System.Collections;
using Reown.Sign.Unity;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Sample.UI
{
    public class SettingsInfoItem : VisualElement
    {
        public new class UxmlFactory : UxmlFactory<SettingsInfoItem, UxmlTraits> { }

        public new class UxmlTraits : VisualElement.UxmlTraits
        {
            UxmlStringAttributeDescription label =
                new()
                    { name = "label", defaultValue = "Label" };
            UxmlStringAttributeDescription value =
                new()
                    { name = "value", defaultValue = "Value" };
            
            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                var item = ve as SettingsInfoItem;
                
                item.Label = label.GetValueFromBag(bag, cc);
                item.Value = value.GetValueFromBag(bag, cc);
            }
        }
        
        public string Label { get => _labelLabel.text ?? ""; set => _labelLabel.text = value; }
        public string Value { get => _valueLabel.text ?? ""; set => _valueLabel.text = value; }
        
        public Clickable Clickable
        {
            get => _clickable;
            set
            {
                _clickable = value;
                this.AddManipulator(value);
            }
        }
        
        public event Action Clicked
        {
            add
            {
                if (Clickable == null)
                    Clickable = new Clickable(value);
                else
                    Clickable.clicked += value;
            }
            remove
            {
                if (Clickable == null)
                    return;
                Clickable.clicked -= value;
            }
        }

        private Clickable _clickable;
        private Label _labelLabel;
        private Label _valueLabel;
        private Label _copiedLabel;
        
        private Coroutine _copiedCoroutine;
        
        public SettingsInfoItem()
        {
            var asset = Resources.Load<VisualTreeAsset>("SettingsInfoItem/SettingsInfoItem");
            asset.CloneTree(this);
            
            _labelLabel = this.Q<Label>("Label");
            _valueLabel = this.Q<Label>("Value");
            _copiedLabel = this.Q<Label>("Copied");
            
            Clicked += ClickedHandler;
        }   
        
        public SettingsInfoItem(string label, string value) : this()
        {
            Label = label;
            Value = value;
        }
        
        private void ClickedHandler()
        {
            GUIUtility.systemCopyBuffer = Value;

            if (_copiedCoroutine != null)
                UnityEventsDispatcher.Instance.StopCoroutine(_copiedCoroutine);
            
            _copiedCoroutine = UnityEventsDispatcher.Instance.StartCoroutine(ShowCopiedLabel());
        }

        private IEnumerator ShowCopiedLabel()
        {
            _valueLabel.style.display = DisplayStyle.None;
            _copiedLabel.style.display = DisplayStyle.Flex;
            
            yield return new WaitForSeconds(1.5f);
            
            _valueLabel.style.display = DisplayStyle.Flex;
            _copiedLabel.style.display = DisplayStyle.None;
            
            _copiedCoroutine = null;
        }
    }
}