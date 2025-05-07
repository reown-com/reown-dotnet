using UnityEngine;
using UnityEngine.UIElements;

namespace Sample
{
    public class Notification : MonoBehaviour
    {
        [SerializeField] private UIDocument _uiDocument;

        public VisualElement NotificationContainer;

        private Label _messageLabel;
        private Button _buttonHide;

        public static Notification Instance
        {
            get
            {
                if (_instance == null)
                    _instance = FindObjectOfType<Notification>(true);

                return _instance;
            }
        }

        private static Notification _instance;

        private void Awake()
        {
            NotificationContainer = _uiDocument.rootVisualElement.Q<VisualElement>("NotificationContainer");
            _messageLabel = _uiDocument.rootVisualElement.Q<Label>("NotificationText");
            _buttonHide = _uiDocument.rootVisualElement.Q<Button>("NotificationButton");

            _buttonHide.clicked += OnButtonHide;
        }

        public static void ShowMessage(string message)
        {
            Instance.Show(message);
        }

        public void Show(string message)
        {
            Debug.Log(message, this);

            _messageLabel.text = message;
            NotificationContainer.style.display = DisplayStyle.Flex;
        }

        public static void Hide()
        {
            Instance.NotificationContainer.style.display = DisplayStyle.None;
        }

        public void OnButtonHide()
        {
            Hide();
        }
    }
}