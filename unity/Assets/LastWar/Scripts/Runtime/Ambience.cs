using UnityEngine;

namespace LastWar
{
    /// <summary>
    /// The sounds of the place around you: waves breaking louder the nearer you are to the beach, birds in the green
    /// and autumn maps (quiet in the snow, where the wind blows instead), a soft wind everywhere that grows on high ground.
    /// Off in the plane and while falling (the plane / fall have their own sounds). CC0 recordings (CREDITS.md).
    /// </summary>
    public class Ambience : MonoBehaviour
    {
        AudioSource waves, birds, wind; PlayerController player;

        public static void Begin(PlayerController p)
        {
            var a = new GameObject("Ambience").AddComponent<Ambience>(); a.player = p;
            a.waves = a.Loop("waves"); a.birds = a.Loop("birds"); a.wind = a.Loop("wind");
        }

        AudioSource Loop(string name)
        {
            var c = Resources.Load<AudioClip>("Audio/" + name); if (c == null) return null;
            var s = gameObject.AddComponent<AudioSource>(); s.clip = c; s.loop = true; s.spatialBlend = 0f; s.volume = 0f;
            s.time = Random.Range(0f, c.length * 0.9f); s.Play(); return s;
        }

        void Update()
        {
            if (player == null || player.fighter == null) return;
            var cam = Camera.main; Vector3 p = cam != null ? cam.transform.position : player.transform.position;
            bool onFoot = player.fighter.air == 0 || player.fighter.air == 3;
            float k = 1f - Mathf.Exp(-Time.deltaTime * 1.5f);
            float shore = MapBuilder.ShoreDistance(p);
            float up = Mathf.Max(0f, p.y - MapBuilder.SeaLevel);
            Set(waves, onFoot ? Mathf.Clamp01(1f - shore / 70f) * 0.55f : 0f, k);
            bool snow = MapBuilder.Look == "snow";
            Set(birds, onFoot && !snow ? 0.16f * Mathf.Clamp01(1f - (shore < 25f ? (25f - shore) / 40f : 0f)) : 0f, k);
            Set(wind, onFoot ? (snow ? 0.075f : 0.03f) + Mathf.Clamp01(up / 60f) * 0.04f : 0f, k);
        }

        static void Set(AudioSource s, float v, float k) { if (s != null) s.volume = Mathf.Lerp(s.volume, v, k); }
    }
}
