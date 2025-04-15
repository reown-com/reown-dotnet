using System;
using System.Collections.Generic;
using System.IO;
using Reown.Core.Common.Logging;
using Reown.Core.Models.Publisher;
using Reown.Sign.Interfaces;
using Reown.Sign.Models;
using Reown.Sign.Models.Engine.Events;
using Reown.Sign.Unity.Utils;
using UnityEngine;

namespace Reown.Sign.Unity
{
    public class Linker : IDisposable
    {
        private readonly SignClientUnity _signClient;

        protected bool disposed;

        public Linker(SignClientUnity signClient)
        {
            _signClient = signClient;

            RegisterEventListeners();
        }

        private void RegisterEventListeners()
        {
            _signClient.SessionRequestSentUnity += SessionRequestSentHandler;
        }

        public static void OpenSessionProposalDeepLink(string uri, string nativeRedirect)
        {
            if (string.IsNullOrWhiteSpace(uri))
                throw new ArgumentException("[Linker] Uri cannot be empty.");

#if UNITY_EDITOR && (UNITY_IOS || UNITY_ANDROID)
            // In editor we cannot open _mobile_ deep links, so we just log the uri
            Debug.Log($"[Linker] Requested to open mobile deep link. The uri: {uri}");
#else

            if (string.IsNullOrWhiteSpace(nativeRedirect))
                throw new Exception(
                    $"[Linker] No link found for {Application.platform} platform.");

            var url = BuildConnectionDeepLink(nativeRedirect, uri);

            ReownLogger.Log($"[Linker] Opening URL {url}");

            Application.OpenURL(url);
#endif
        }

        private void SessionRequestSentHandler(object _, SessionRequestEvent e)
        {
            var session = _signClient.Session.Get(e.Topic);
            OpenSessionRequestDeepLink(session, e.Id);
        }

        public static void OpenSessionRequestDeepLink(Session session, long requestId)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            if (session.Peer.Metadata == null)
                return;

            // ReSharper disable once InlineOutVariableDeclaration
            string deeplink;

#if UNITY_STANDALONE
            // On desktop don't use redirect url from peer metadata because it 
            // can include mobile app url when connected via QR code.
            // Instead, we load recent wallet's deeplink which returns native url
            // for active platform (desktop or mobile) from the wallet explorer
            if (!TryGetRecentWalletDeepLink(out deeplink))
                return;
#else
            var redirectNative = session.Peer.Metadata.Redirect?.Native;

            if (string.IsNullOrWhiteSpace(redirectNative))
            {
                if (!TryGetRecentWalletDeepLink(out deeplink))
                    return;

                Debug.LogWarning(
                    $"[Linker] No redirect found for {session.Peer.Metadata.Name}. Using deep link from the Recent Wallet."
                );
            }
            else
            {
                deeplink = redirectNative;
                ReownLogger.Log($"[Linker] Open native deep link: {deeplink}");
            }
#endif

            if (!deeplink.Contains("://"))
            {
                deeplink = deeplink.Replace("/", "").Replace(":", "");
                deeplink = $"{deeplink}://";
            }

            if (!deeplink.EndsWith("wc"))
                deeplink = Path.Combine(deeplink, "wc");

            deeplink = $"{deeplink}?requestId={requestId}&sessionTopic={session.Topic}";

            Debug.Log($"[Linker] Opening URL {deeplink}");
            Application.OpenURL(deeplink);
        }

        public static string BuildConnectionDeepLink(string appLink, string wcUri)
        {
            if (string.IsNullOrWhiteSpace(wcUri))
                throw new ArgumentException("[Linker] Uri cannot be empty.");

            if (string.IsNullOrWhiteSpace(appLink))
                throw new ArgumentException("[Linker] Native link cannot be empty.");

            var safeAppUrl = appLink;
            if (!safeAppUrl.Contains("://"))
            {
                safeAppUrl = safeAppUrl.Replace("/", "").Replace(":", "");
                safeAppUrl = $"{safeAppUrl}://";
            }

            if (!safeAppUrl.EndsWith('/'))
                safeAppUrl = $"{safeAppUrl}/";

            var encodedWcUrl = Uri.EscapeDataString(wcUri);

            return $"{safeAppUrl}wc?uri={encodedWcUrl}";
        }

        public static bool CanOpenURL(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            try
            {
#if !UNITY_EDITOR && UNITY_IOS
                return _CanOpenURL(url);
#elif !UNITY_EDITOR && UNITY_ANDROID
                using var urlCheckerClass = new AndroidJavaClass("com.reown.sign.unity.Linker");
                using var unityPlayerClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var currentContext = unityPlayerClass.GetStatic<AndroidJavaObject>("currentActivity");
                var result = urlCheckerClass.CallStatic<bool>("canOpenURL", currentContext, url);
                return result;
#endif
            }
            catch (Exception e)
            {
                ReownLogger.LogError($"[Linker] Exception for url {url}: {e.Message}");
            }

            return false;
        }

#if !UNITY_EDITOR && UNITY_IOS
        [System.Runtime.InteropServices.DllImport("__Internal")]
        public static extern bool _CanOpenURL(string url);
#endif

        private static bool TryGetRecentWalletDeepLink(out string deeplink)
        {
            deeplink = null;

            deeplink = PlayerPrefs.GetString("RE_RECENT_WALLET_DEEPLINK");

            if (string.IsNullOrWhiteSpace(deeplink))
                return false;

            return deeplink != null;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed) return;

            if (disposing)
                _signClient.SessionRequestSent -= SessionRequestSentHandler;

            disposed = true;
        }
    }
}