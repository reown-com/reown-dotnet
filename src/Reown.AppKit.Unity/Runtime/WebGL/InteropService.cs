using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AOT;
using Newtonsoft.Json;
using UnityEngine;
using Reown.AppKit.Unity.Utils;

namespace Reown.AppKit.Unity.WebGl
{
    public class InteropService
    {
        private static readonly Dictionary<int, PendingInteropCall> PendingInteropCalls = new();

        private readonly ExternalMethod _externalMethod;
        private readonly ExternalMethodAsync _asyncExternalMethod;

        private readonly JsonConverter[] _jsonConverts =
        {
            new ByteArrayJsonConverter()
        };

        public InteropService(ExternalMethod externalMethod, ExternalMethodAsync asyncExternalMethod)
        {
            _externalMethod = externalMethod;
            _asyncExternalMethod = asyncExternalMethod;
        }

        private string SerializeRequestParameter<TReq>(TReq requestParameter)
        {
            if (Equals(requestParameter, default(TReq)))
                return null;

            if (typeof(TReq) == typeof(string))
                return requestParameter as string;

            return JsonConvert.SerializeObject(requestParameter, _jsonConverts);
        }

        private static PendingInteropCall CreatePendingCall<TRes>()
        {
            var tcs = new TaskCompletionSource<object>();
            return new PendingInteropCall(typeof(TRes), tcs);
        }

        private static object ProcessResponseData(string responseData, Type targetType)
        {
            if (responseData == null)
                return null;

            if (targetType == typeof(string))
            {
                return responseData.Trim('"');
            }
            else if (targetType == typeof(int) && int.TryParse(responseData, out var intResult))
            {
                return intResult;
            }
            else if (targetType == typeof(float) && float.TryParse(responseData, out var floatResult))
            {
                return floatResult;
            }
            else if (targetType == typeof(double) && double.TryParse(responseData, out var doubleResult))
            {
                return doubleResult;
            }
            else if (targetType == typeof(bool) && bool.TryParse(responseData, out var boolResult))
            {
                return boolResult;
            }
            else if (targetType == typeof(char) && char.TryParse(responseData, out var charResult))
            {
                return charResult;
            }
            else if (targetType == typeof(BigInteger) && BigInteger.TryParse(responseData, out var bigIntResult))
            {
                return bigIntResult;
            }
            else if (targetType != typeof(void))
            {
                return JsonConvert.DeserializeObject(responseData, targetType);
            }

            return null;
        }

        public TRes InteropCall<TReq, TRes>(string methodName, TReq requestParameter)
        {
            try
            {
                var paramStr = SerializeRequestParameter(requestParameter);
                var responseData = _externalMethod(methodName, paramStr);

                if (responseData == null)
                {
                    return default;
                }

                try
                {
                    var result = ProcessResponseData(responseData, typeof(TRes));
                    return (TRes)result;
                }
                catch (Exception e)
                {
                    throw new FormatException($"Failed to deserialize response: {responseData}", e);
                }
            }
            catch (Exception e)
            {
                if (e is FormatException)
                    throw;

                throw new InteropException(e.Message);
            }
        }

        public async Task<TRes> InteropCallAsync<TReq, TRes>(string methodName, TReq requestParameter, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var id = Guid.NewGuid().GetHashCode();
            var pendingInteropCall = CreatePendingCall<TRes>();
            PendingInteropCalls.Add(id, pendingInteropCall);

            var cancellationTokenRegistration = cancellationToken.Register(() =>
            {
                if (!PendingInteropCalls.TryGetValue(id, out var call))
                    return;

                call.TaskCompletionSource.TrySetCanceled();
                PendingInteropCalls.Remove(id);
            });

            try
            {
                var paramStr = SerializeRequestParameter(requestParameter);
                _asyncExternalMethod(id, methodName, paramStr, TcsCallback);

                var result = await pendingInteropCall.TaskCompletionSource.Task;
                return (TRes)result;
            }
            finally
            {
                await cancellationTokenRegistration.DisposeAsync();
                PendingInteropCalls.Remove(id);
            }
        }

        [MonoPInvokeCallback(typeof(ExternalMethodCallback))]
        public static void TcsCallback(int id, string responseData, string responseError = null)
        {
            if (!PendingInteropCalls.TryGetValue(id, out var pendingCall))
            {
                Debug.LogError("No pending call found for id: " + id);
                return;
            }

            if (!string.IsNullOrEmpty(responseError))
            {
                try
                {
                    var error = JsonConvert.DeserializeObject<InteropCallError>(responseError);
                    pendingCall.TaskCompletionSource.SetException(new InteropException(error.message));
                    PendingInteropCalls.Remove(id);
                    return;
                }
                catch (Exception)
                {
                    pendingCall.TaskCompletionSource.SetException(new FormatException($"Unable to parse error response: {responseError}"));
                    PendingInteropCalls.Remove(id);
                    return;
                }
            }

            if (responseData == null)
            {
                pendingCall.TaskCompletionSource.SetResult(null);
                PendingInteropCalls.Remove(id);
                return;
            }

            try
            {
                var result = ProcessResponseData(responseData, pendingCall.ResType);
                pendingCall.TaskCompletionSource.SetResult(result);
            }
            catch (Exception e)
            {
                pendingCall.TaskCompletionSource.SetException(e);
            }
            finally
            {
                PendingInteropCalls.Remove(id);
            }
        }

        public delegate void ExternalMethodAsync(int id, string methodName, string parameter, ExternalMethodCallback callback);

        public delegate string ExternalMethod(string methodName, string parameter);

        public delegate void ExternalMethodCallback(int id, string responseData, string responseError = null);

        private readonly struct PendingInteropCall
        {
            public readonly Type ResType;
            public readonly TaskCompletionSource<object> TaskCompletionSource;

            public PendingInteropCall(Type resType, TaskCompletionSource<object> taskCompletionSource)
            {
                ResType = resType;
                TaskCompletionSource = taskCompletionSource;
            }
        }
    }
}