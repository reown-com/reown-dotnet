using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Reown.AppKit.Unity.WebGl.Viem
{
#if UNITY_WEBGL
    public class ViemInterop
    {
        [DllImport("__Internal")]
        private static extern void ViemCall(int id, string methodName, string payload, InteropService.ExternalMethodCallback callback);

        private static readonly InteropService InteropService = new(ViemCall);

        public static Task<TRes> InteropCallAsync<TReq, TRes>(string methodName, TReq requestParameter, CancellationToken cancellationToken = default)
        {
            return InteropService.InteropCallAsync<TReq, TRes>(methodName, requestParameter, cancellationToken);
        }

        // -- Parse ABI ------------------------------------------------

        public static Task<string> ParseAbiAsync(params string[] abi)
        {
            return InteropCallAsync<string[], string>("parseAbi", abi);
        }
    }
#endif
}