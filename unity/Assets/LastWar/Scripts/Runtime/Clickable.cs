using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LastWar
{
    /// <summary>Tap handler that only listens to clicks, so drags still reach a ScrollRect behind it (EventTrigger swallows them).</summary>
    public class Clickable : MonoBehaviour, IPointerClickHandler
    {
        public Action onClick;
        public static void Add(GameObject go, Action a) { var c = go.AddComponent<Clickable>(); c.onClick = a; }
        public void OnPointerClick(PointerEventData e) { if (!e.dragging) onClick?.Invoke(); }
    }
}
