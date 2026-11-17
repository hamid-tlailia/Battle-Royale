using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LastWar
{
    /// <summary>Tap handler that only listens to clicks, so drags still reach a ScrollRect behind it (EventTrigger swallows them).</summary>
    public class Clickable : MonoBehaviour, IPointerClickHandler
    {
        public Action onClick;
        public bool silent; // (PLAY has its own sound)
        public static Clickable Add(GameObject go, Action a) { var c = go.AddComponent<Clickable>(); c.onClick = a; return c; }
        public void OnPointerClick(PointerEventData e) { if (e.dragging) return; if (!silent) Sfx.Play2D("ui_click", 0.55f); onClick?.Invoke(); }
    }
}
