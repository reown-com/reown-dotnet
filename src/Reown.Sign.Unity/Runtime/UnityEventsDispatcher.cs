using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Reown.Sign.Unity.Utils;
using UnityEngine;

namespace Reown.Sign.Unity
{
    public sealed class UnityEventsDispatcher : MonoBehaviour
    {
        private static UnityEventsDispatcher _instance;

        // TODO: Make this configurable
        private readonly IEnumerator _tickYieldInstruction = new WaitForNthFrame(3);
        private Action _tick;

        private Coroutine _tickCoroutine;

        public static UnityEventsDispatcher Instance
        {
            get
            {
                if (_instance != null) return _instance;

                var go = new GameObject("[Reown] UnityEventsDispatcher")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                DontDestroyOnLoad(go);

                _instance = go.AddComponent<UnityEventsDispatcher>();

                return _instance;
            }
        }

        private bool TickHasListeners
        {
            get => _tick?.GetInvocationList().Length > 0;
        }

        private void OnDestroy()
        {
            Destroy(gameObject);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            ApplicationFocus?.Invoke(hasFocus);
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            ApplicationPause?.Invoke(pauseStatus);
        }

        private void OnApplicationQuit()
        {
            ApplicationQuit?.Invoke();
        }

        /// <summary>
        ///     Invoked every 3rd frame on the main thread.
        /// </summary>
        public event Action Tick
        {
            add
            {
                var wasEmpty = !TickHasListeners;

                _tick += value;

                if (!wasEmpty)
                    return;

                try
                {
                    _tickCoroutine = StartCoroutine(TickRoutine());
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
            remove
            {
                _tick -= value;

                if (!TickHasListeners && _tickCoroutine != null)
                    StopCoroutine(_tickCoroutine);
            }
        }

        public static void InvokeNextFrame(Action action)
        {
            Instance.StartCoroutine(InvokeNextFrameRoutine(action));
        }

        /// <summary>
        ///     Asynchronously waits for the specified number of seconds on the Unity main thread.
        /// </summary>
        /// <param name="seconds">The delay duration, in seconds.</param>
        /// <param name="cancellationToken">Optional token that can be used to cancel the wait before it completes.</param>
        /// <returns>A task that completes after the delay has elapsed or the operation is cancelled.</returns>
        /// <remarks>
        ///     For Unity 6000.0 or newer this method delegates to <see cref="Awaitable.WaitForSecondsAsync"/>.
        ///     For earlier Unity versions it falls back to a coroutine-based implementation.
        /// </remarks>
        public static async Task WaitForSecondsAsync(float seconds, CancellationToken cancellationToken = default)
        {
#if UNITY_6000_0_OR_NEWER
            await Awaitable.WaitForSecondsAsync(seconds, cancellationToken);
#else
            var tcs = new TaskCompletionSource<bool>(cancellationToken);
            Instance.StartCoroutine(WaitForSecondsRoutine(seconds, tcs));
            await tcs.Task;
#endif
        }

        private static IEnumerator InvokeNextFrameRoutine(Action action)
        {
            yield return null;
            action?.Invoke();
        }

        private static IEnumerator WaitForSecondsRoutine(float seconds, TaskCompletionSource<bool> tcs)
        {
            yield return new WaitForSeconds(seconds);
            tcs.SetResult(true);
        }

        /// <summary>
        ///     Invoked when the application is paused or resumed.
        /// </summary>
        public event Action<bool> ApplicationPause;

        /// <summary>
        ///     Invoked when the application gains or loses focus.
        /// </summary>
        public event Action<bool> ApplicationFocus;

        /// <summary>
        ///     Invoked when the application is quitting.
        /// </summary>
        public event Action ApplicationQuit;

        private IEnumerator TickRoutine()
        {
            while (enabled)
            {
                _tick?.Invoke();
                yield return _tickYieldInstruction;
            }
        }
    }
}
