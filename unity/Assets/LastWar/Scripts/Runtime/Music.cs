using UnityEngine;

namespace LastWar
{
    /// <summary>
    /// Light, upbeat loop (original, Resources/Audio/lobby_music) under the lobby and the loading card;
    /// it keeps playing into the match and fades out once the drop plane appears.
    /// </summary>
    public class Music : MonoBehaviour
    {
        static Music current;
        AudioSource src; float fade = -1, fadeAt;

        public static void PlayLobby()
        {
            if (current != null) { current.fade = -1; current.src.volume = 0.26f; if (!current.src.isPlaying) current.src.Play(); return; }
            var clip = Resources.Load<AudioClip>("Audio/lobby_music");
            if (clip == null) return;
            var go = new GameObject("Music"); DontDestroyOnLoad(go);
            current = go.AddComponent<Music>();
            current.src = go.AddComponent<AudioSource>();
            current.src.clip = clip; current.src.loop = true; current.src.spatialBlend = 0f; current.src.volume = 0.26f;
            current.src.Play();
        }

        /// <summary>fade out over ~1.5 s (the plane is here)</summary>
        public static void Stop(float after = 1.2f) { if (current != null && current.fade < 0) { current.fade = 1.5f; current.fadeAt = Time.unscaledTime + after; } }

        void Update()
        {
            if (fade < 0 || Time.unscaledTime < fadeAt) return;
            src.volume = Mathf.MoveTowards(src.volume, 0f, Time.unscaledDeltaTime * 0.26f / 1.5f);
            if (src.volume <= 0.001f) { Destroy(gameObject); if (current == this) current = null; }
        }
    }
}
