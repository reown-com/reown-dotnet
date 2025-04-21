using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Reown.AppKit.Unity.WebGl.Viem
{
#if UNITY_WEBGL
    public class ViemInterop
    {
        [DllImport("__Internal")]
        private static extern string ViemCall(string methodName, string parameters);

        [DllImport("__Internal")]
        private static extern void ViemCallAsync(int id, string methodName, string parameters, InteropService.ExternalMethodCallback callback);

        private static readonly InteropService InteropService = new(ViemCall, ViemCallAsync);

        public static Task<TRes> InteropCallAsync<TReq, TRes>(string methodName, TReq requestParameter, CancellationToken cancellationToken = default)
        {
            return InteropService.InteropCallAsync<TReq, TRes>(methodName, requestParameter, cancellationToken);
        }

        public static TRes InteropCall<TReq, TRes>(string methodName, TReq requestParameter)
        {
            return InteropService.InteropCall<TReq, TRes>(methodName, requestParameter);
        }

        // -- Parse ABI ------------------------------------------------

        public static string ParseAbi(params string[] abi)
        {
            return InteropCall<string[], string>("parseAbi", abi);
        }
    }
#endif
}