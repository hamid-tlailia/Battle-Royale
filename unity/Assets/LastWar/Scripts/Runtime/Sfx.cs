using System.Collections.Generic;
using UnityEngine;

namespace LastWar
{
    /// <summary>Recorded sounds from Resources/Audio, played in 3D at a point.</summary>
    public static class Sfx
    {
        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        public static void Play(string name, Vector3 at, float volume = 1f)
        {
            if (!clips.TryGetValue(name, out AudioClip c)) { c = Resources.Load<AudioClip>("Audio/" + name); clips[name] = c; }
            if (c == null) return;
            var go = new GameObject("sfx");
            go.transform.position = at;
            var src = go.AddComponent<AudioSource>();
            src.clip = c; src.volume = volume; src.spatialBlend = 0.85f; src.minDistance = 4f; src.maxDistance = 220f;
            src.rolloffMode = AudioRolloffMode.Logarithmic; src.pitch = Random.Range(0.94f, 1.06f);
            src.Play();
            Object.Destroy(go, c.length + 0.1f);
        }
    }
}
