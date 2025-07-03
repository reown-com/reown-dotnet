using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Reown.AppKit.Unity.Tests
{
    public static class UtkUtils
    {
        public static async UniTask TapAsync(VisualElement element, CancellationToken ct = default)
        {
            if (element == null)
                throw new ArgumentNullException(nameof(element));

            if (element.panel == null)
                throw new InvalidOperationException("Element must be attached to a panel first.");

            // Ensure styles & layout updated
            await UniTask.NextFrame(ct);

            var pos = element.worldBound.center;

            // Sequence: Move → Wait → Touch Down → Wait → Touch Up → Wait
            SendTouch(EventType.TouchMove, pos, element);
            await UniTask.NextFrame(ct);

            SendTouch(EventType.TouchDown, pos, element);
            await UniTask.NextFrame(ct);

            SendTouch(EventType.TouchUp, pos, element);
            await UniTask.NextFrame(ct);
        }

        private static void SendTouch(EventType rawType, Vector2 pos, VisualElement element)
        {
            var systemEvent = new UnityEngine.Event
            {
                type = rawType,
                mousePosition = pos
            };

            EventBase pointerEvent = rawType switch
            {
                EventType.TouchMove => PointerMoveEvent.GetPooled(systemEvent),
                EventType.TouchDown => PointerDownEvent.GetPooled(systemEvent),
                EventType.TouchUp => PointerUpEvent.GetPooled(systemEvent),
                _ => throw new ArgumentOutOfRangeException(nameof(rawType))
            };

            element.SendEvent(pointerEvent);
            pointerEvent.Dispose();
        }
    }
}