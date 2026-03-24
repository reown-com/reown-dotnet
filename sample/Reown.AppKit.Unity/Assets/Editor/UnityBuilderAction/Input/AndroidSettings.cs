using System;
using System.Collections.Generic;
using UnityEditor;

namespace UnityBuilderAction.Input
{
  public static class AndroidSettings
  {
    public static void Apply(Dictionary<string, string> options)
    {
      if (options.TryGetValue("androidKeystoreName", out var keystoreName) && !string.IsNullOrEmpty(keystoreName))
      {
        PlayerSettings.Android.useCustomKeystore = true;
        PlayerSettings.Android.keystoreName = keystoreName;
      }

      if (options.TryGetValue("androidKeystorePass", out var keystorePass) && !string.IsNullOrEmpty(keystorePass))
        PlayerSettings.Android.keystorePass = keystorePass;

      if (options.TryGetValue("androidKeyaliasName", out var keyaliasName) && !string.IsNullOrEmpty(keyaliasName))
        PlayerSettings.Android.keyaliasName = keyaliasName;

      if (options.TryGetValue("androidKeyaliasPass", out var keyaliasPass) && !string.IsNullOrEmpty(keyaliasPass))
        PlayerSettings.Android.keyaliasPass = keyaliasPass;
      
      if (options.TryGetValue("androidTargetSdkVersion", out var androidTargetSdkVersion) && !string.IsNullOrEmpty(androidTargetSdkVersion))
      {
          var targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
          try
          {
              targetSdkVersion =
                  (AndroidSdkVersions) Enum.Parse(typeof(AndroidSdkVersions), androidTargetSdkVersion);
          }
          catch
          {
              UnityEngine.Debug.Log("Failed to parse androidTargetSdkVersion! Fallback to AndroidApiLevelAuto");
          }
          PlayerSettings.Android.targetSdkVersion = targetSdkVersion;
      }

      if (options.TryGetValue("androidExportType", out var androidExportType) && !string.IsNullOrEmpty(androidExportType))
      {
        switch (androidExportType)
        {
          case "androidStudioProject":
            EditorUserBuildSettings.exportAsGoogleAndroidProject = true;
            EditorUserBuildSettings.buildAppBundle = false;
            break;
          case "androidAppBundle":
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            EditorUserBuildSettings.buildAppBundle = true;
            break;
          case "androidPackage":
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            EditorUserBuildSettings.buildAppBundle = false;
            break;
        }
      }

      if (options.TryGetValue("androidSymbolType", out var symbolType) && !string.IsNullOrEmpty(symbolType))
      {
        switch (symbolType)
        {
          case "public":
            EditorUserBuildSettings.androidCreateSymbols = AndroidCreateSymbols.Public;
            break;
          case "debugging":
            EditorUserBuildSettings.androidCreateSymbols = AndroidCreateSymbols.Debugging;
            break;
          case "none":
            EditorUserBuildSettings.androidCreateSymbols = AndroidCreateSymbols.Disabled;
            break;
        }
      }
    }
  }
}
