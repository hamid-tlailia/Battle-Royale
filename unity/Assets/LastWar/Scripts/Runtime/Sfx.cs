using System.Collections.Generic;
using UnityEngine;

namespace LastWar
{
    /// <summary>Recorded sounds from Resources/Audio, played in 3D at a point (or flat, for the player's own hands and feet).</summary>
    public static class Sfx
    {
        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        static AudioClip Clip(string name)
        {
            if (!clips.TryGetValue(name, out AudioClip c)) { c = Resources.Load<AudioClip>("Audio/" + name); clips[name] = c; }
            return c;
        }

        public static void Play(string name, Vector3 at, float volume = 1f, float pitch = 1f, float maxDistance = 220f)
        {
            AudioClip c = Clip(name);
            if (c == null) return;
            var go = new GameObject("sfx");
            go.transform.position = at;
            var src = go.AddComponent<AudioSource>();
            src.clip = c; src.volume = volume; src.spatialBlend = 0.85f; src.minDistance = Mathf.Min(4f, maxDistance / 6f); src.maxDistance = maxDistance;
            src.rolloffMode = maxDistance < 100f ? AudioRolloffMode.Linear : AudioRolloffMode.Logarithmic; src.pitch = pitch * Random.Range(0.94f, 1.06f);
            src.Play();
            Object.Destroy(go, c.length / Mathf.Max(0.3f, src.pitch) + 0.1f);
        }

        /// <summary>not placed in the world: your own gun handling, scope, footsteps</summary>
        public static void Play2D(string name, float volume = 1f, float pitch = 1f)
        {
            AudioClip c = Clip(name);
            if (c == null) return;
            var go = new GameObject("sfx2d");
            var src = go.AddComponent<AudioSource>();
            src.clip = c; src.volume = volume; src.spatialBlend = 0f; src.pitch = pitch;
            src.Play();
            Object.Destroy(go, c.length / Mathf.Max(0.3f, pitch) + 0.1f);
        }

        /// <summary>one footstep: grass / sand outdoors, stone on house floors; soft when crouched</summary>
        public static void Step(Vector3 at, bool mine, bool soft, bool stone)
        {
            string n = (stone ? "stone" : "step") + Random.Range(1, 7);
            float v = soft ? 0.45f : 0.9f, p = Random.Range(0.92f, 1.08f);
            if (mine) Play2D(n, v * 0.8f, p); else Play(n, at, v, p, 26f);
        }
    }
}
