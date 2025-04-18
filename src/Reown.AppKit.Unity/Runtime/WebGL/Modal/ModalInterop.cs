using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using AOT;
using Newtonsoft.Json;
using UnityEngine;

namespace Reown.AppKit.Unity.WebGl.Modal
{
    public static class ModalInterop
    {
#if UNITY_WEBGL
        [DllImport("__Internal")]
#endif
        private static extern string ModalCall(string methodName, string parameters);

#if UNITY_WEBGL
        [DllImport("__Internal")]
#endif
        private static extern void ModalCallAsync(int id, string methodName, string parameters, InteropService.ExternalMethodCallback callback);

#if UNITY_WEBGL
        [DllImport("__Internal")]
#endif
        private static extern void ModalSubscribeState(Action<string> callback);

#if UNITY_WEBGL
        [DllImport("__Internal")]
#endif
        private static extern void ModalSubscribeAccount(Action<string> callback);

        public static event Action<ModalState> StateChanged;

        public static event Action<WebAppKitAccount> AccountChanged;

        private static readonly InteropService InteropService = new(ModalCall, ModalCallAsync);

        private static bool _eventsInitialised;

        public static Task<TRes> InteropCallAsync<TReq, TRes>(string methodName, TReq requestParameter, CancellationToken cancellationToken = default)
        {
            return InteropService.InteropCallAsync<TReq, TRes>(methodName, requestParameter, cancellationToken);
        }

        public static TRes InteropCall<TReq, TRes>(string methodName, TReq requestParameter)
        {
            return InteropService.InteropCall<TReq, TRes>(methodName, requestParameter);
        }

        // -- Events --------------------------------------------------

        public static void InitializeEvents()
        {
            if (_eventsInitialised)
                return;

            ModalSubscribeState(SubscribeStateCallback);
            ModalSubscribeAccount(SubscribeAccountCallback);

            _eventsInitialised = true;
        }

        [MonoPInvokeCallback(typeof(Action<string>))]
        public static void SubscribeStateCallback(string stateJson)
        {
            var state = JsonConvert.DeserializeObject<ModalState>(stateJson);
            StateChanged?.Invoke(state);
        }

        [MonoPInvokeCallback(typeof(Action<string>))]
        public static void SubscribeAccountCallback(string accountJson)
        {
            var account = JsonConvert.DeserializeObject<WebAppKitAccount>(accountJson);
            AccountChanged?.Invoke(account);
        }


        // -- Open Modal ----------------------------------------------

        public static async void Open(OpenModalParameters parameters = null)
        {
            await OpenModalAsync(parameters);
        }

        public static async Task OpenModalAsync(OpenModalParameters parameters = null)
        {
            await InteropCallAsync<OpenModalParameters, object>(ModalMethods.Open, parameters);
        }


        // -- Close Modal ---------------------------------------------

        public static async void Close()
        {
            await CloseModalAsync();
        }

        public static async Task CloseModalAsync()
        {
            await InteropCallAsync<object, object>(ModalMethods.Close, null);
        }


        // -- Get Account --------------------------------------------

        public static WebAppKitAccount GetAccount()
        {
            var result = InteropCall<object, WebAppKitAccount>(ModalMethods.GetAccount, "eip155");
            Debug.Log(JsonConvert.SerializeObject(result));
            return result;
        }
    }
}