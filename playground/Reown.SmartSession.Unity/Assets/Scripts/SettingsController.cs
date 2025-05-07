using Reown.AppKit.Unity;
using Sample.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Button = UnityEngine.UIElements.Button;

namespace Sample
{
    public class SettingsController : MonoBehaviour
    {
        private Button _settingsButton;
        private VisualElement _settingsView;
        private VisualElement _background;
        
        private VisualElement _scrollContentContainer;
        private SettingsInfoItem _clientIdInfoItem;

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            
            var uiDocument = GetComponent<UIDocument>();
            
            _settingsButton = uiDocument.rootVisualElement.Q<Button>("SettingsButton");
            _settingsView = uiDocument.rootVisualElement.Q<VisualElement>("SettingsView");
            _background = uiDocument.rootVisualElement.Q<VisualElement>("Background");
            
            _settingsButton.clicked += OnSettingsButtonClicked;
            
#if !UNITY_WEBGL
            SceneManager.sceneLoaded += OnSceneLoaded;
#endif
            
            AddInfoItems();
        }

        private void AddInfoItems()
        {
            _scrollContentContainer = _settingsView.Q<VisualElement>("unity-content-container");

            var appVersion = new SettingsInfoItem("App Version", Application.version);
            _scrollContentContainer.Add(appVersion);
        }
        
        private async void UpdateClientIdInfoItem()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return;
#endif
            
            if (_clientIdInfoItem != null)
            {
                _scrollContentContainer.Remove(_clientIdInfoItem);
            }

            if (AppKit.Instance == null || !AppKit.IsInitialized)
                return;
                
            var clientId = await AppKit.Instance.SignClient.CoreClient.Crypto.GetClientId();
            _clientIdInfoItem = new SettingsInfoItem("Client ID", clientId);
            _scrollContentContainer.Add(_clientIdInfoItem);
        }
        
        
#if !UNITY_WEBGL
        private void OnSceneLoaded(Scene arg0, LoadSceneMode arg1)
        {
            UpdateClientIdInfoItem();
        }
#endif
        
        private void OnSettingsButtonClicked()
        {
            _settingsView.visible = !_settingsView.visible;
            _background.visible = _settingsView.visible;
            _settingsButton.text = _settingsView.visible ? "hide" : "settings";
        }
    }
}