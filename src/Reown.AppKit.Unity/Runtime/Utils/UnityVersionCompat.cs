using System;
using UnityEngine;
using UnityEngine.UIElements;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Reown.AppKit.Unity.Utils
{
    /// <summary>
    /// Centralized compatibility layer for handling Unity version differences.
    /// This class abstracts version-specific APIs to ensure forward compatibility
    /// with future Unity releases.
    /// </summary>
    public static class UnityVersionCompat
    {
        /// <summary>
        /// Registers a one-time callback for an event. On Unity 6+, uses the native
        /// RegisterCallbackOnce. On earlier versions, registers and auto-unregisters.
        /// </summary>
        /// <typeparam name="TEventType">The event type to listen for</typeparam>
        /// <param name="element">The visual element to attach the callback to</param>
        /// <param name="callback">The callback to invoke</param>
        public static void RegisterCallbackOnce<TEventType>(
            this VisualElement element,
            EventCallback<TEventType> callback)
            where TEventType : EventBase<TEventType>, new()
        {
#if UNITY_6000_0_OR_NEWER
            element.RegisterCallbackOnce(callback);
#else
            EventCallback<TEventType> onceCallback = null;
            onceCallback = evt =>
            {
                try
                {
                    callback(evt);
                }
                finally
                {
                    element.UnregisterCallback(onceCallback);
                }
            };
            element.RegisterCallback(onceCallback);
#endif
        }

        /// <summary>
        /// Sets the placeholder text for a TextField. On Unity 6+, uses the native
        /// textEdition.placeholder API. On earlier versions, this is a no-op.
        /// </summary>
        /// <param name="textField">The text field to set placeholder for</param>
        /// <param name="placeholder">The placeholder text</param>
        public static void SetPlaceholder(this TextField textField, string placeholder)
        {
#if UNITY_6000_0_OR_NEWER
            textField.textEdition.placeholder = placeholder;
#else
            // Placeholder API not available in Unity versions before 6.0
            // Consider using a label overlay as a fallback if needed
#endif
        }

        /// <summary>
        /// Checks if the Escape key was pressed this frame.
        /// Automatically handles both New Input System and Legacy Input Manager.
        /// </summary>
        /// <returns>True if Escape was pressed this frame</returns>
        public static bool IsEscapeKeyPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            return keyboard != null && keyboard.escapeKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Escape);
#else
            // No input system available
            return false;
#endif
        }
    }
}