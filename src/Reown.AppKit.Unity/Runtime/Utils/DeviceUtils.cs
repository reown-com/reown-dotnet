using System;
using UnityEngine;

namespace Reown.AppKit.Unity.Utils
{
    public static class DeviceUtils
    {
        public static DeviceType GetDeviceType()
        {
#if UNITY_IOS
            return UnityEngine.iOS.Device.generation.ToString().Contains("iPad")
                ? DeviceType.Tablet
                : DeviceType.Phone;
#elif UNITY_VISIONOS
            return DeviceType.Tablet;
#elif UNITY_ANDROID && UNITY_EDITOR
            return DeviceType.Phone;
#elif UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                var resources = currentActivity.Call<AndroidJavaObject>("getResources");
                var configuration = resources.Call<AndroidJavaObject>("getConfiguration");

                var screenWidthDp = configuration.Get<int>("screenWidthDp");
                var screenHeightDp = configuration.Get<int>("screenHeightDp");

                // Use the smaller dimension to determine if it's a tablet
                var smallestDp = Mathf.Min(screenWidthDp, screenHeightDp);
                
                // Tablets typically have a smallest dimension of 600dp or higher
                return smallestDp >= 600 ? DeviceType.Tablet : DeviceType.Phone;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return DeviceType.Phone;
            }
#elif UNITY_WEBGL
            return DeviceType.Web;
#else
            return DeviceType.Desktop;
#endif
        }
    }

    public enum DeviceType
    {
        Desktop,
        Phone,
        Tablet,
        Web
    }
}