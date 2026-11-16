using UnityEngine;
using UnityEngine.UI;

namespace LastWar
{
    /// <summary>
    /// The web game's victory celebration (also when a bot is the last one standing: WINNER + its name): a gold glow over the screen, VICTORY popping in, #1 / players, kills,
    /// confetti raining down, only the victory music playing - then a countdown and back to the lobby by itself
    /// (or LOBBY now).
    /// </summary>
    public class Celebrate : MonoBehaviour
    {
        const float Seconds = 8f;
        static readonly Color[] Colors = { new Color(1f, .83f, .3f), new Color(1f, .42f, .42f), new Color(.44f, .81f, .44f), new Color(.34f, .66f, .96f), Color.white, new Color(.77f, .55f, 1f) };
        RectTransform box; Text count; AudioSource music; float t;
        struct Bit { public RectTransform rt; public float speed, spin, sway, phase; }
        Bit[] bits;

        public static void Show(Transform full, Transform hudRoot, int kills, int players, string winner = null, int myRank = 0)
        {
            const float K = UiKit.K;
            var shade = UiKit.Img(full, "Victory", null, new Vector2(.5f, .5f), Vector2.zero, Vector2.one, new Color(0, 0, 0, .55f), true);
            UiKit.Fill(shade.rectTransform); shade.rectTransform.SetAsLastSibling();
            var c = shade.gameObject.AddComponent<Celebrate>();
            // the gold glow in the middle
            var glow = UiKit.Img(shade.rectTransform, "glow", UiKit.Disc, new Vector2(.5f, .5f), Vector2.zero, new Vector2(900, 600), new Color(1f, .83f, .3f, .35f));
            glow.rectTransform.pivot = new Vector2(.5f, .5f);
            // confetti
            var rnd = new System.Random();
            c.bits = new Bit[90];
            for (int i = 0; i < c.bits.Length; i++)
            {
                var b = UiKit.Img(shade.rectTransform, "c", null, new Vector2((float)rnd.NextDouble(), 1), Vector2.zero, new Vector2(10, 16), Colors[i % Colors.Length]);
                b.rectTransform.pivot = new Vector2(.5f, .5f);
                c.bits[i] = new Bit { rt = b.rectTransform, speed = 1f / (2.2f + (float)rnd.NextDouble() * 2f), spin = 200f + (float)rnd.NextDouble() * 300f, sway = 40f * K, phase = (float)rnd.NextDouble() * 1.6f };
                b.rectTransform.anchoredPosition = new Vector2(0, 20 * K);
            }
            // the card
            c.box = new GameObject("box", typeof(RectTransform)).GetComponent<RectTransform>();
            c.box.SetParent(shade.rectTransform, false); c.box.anchorMin = c.box.anchorMax = c.box.pivot = new Vector2(.5f, .55f);
            c.box.sizeDelta = new Vector2(700, 300) * K;
            var h1 = UiKit.Txt(c.box, new Vector2(.5f, 1), Vector2.zero, new Vector2(700, 120), 96, TextAnchor.MiddleCenter, new Color(1f, .83f, .3f));
            h1.rectTransform.pivot = new Vector2(.5f, 1); h1.text = winner == null ? "VICTORY" : "WINNER";
            var sh = h1.GetComponent<Shadow>(); sh.effectColor = new Color(.54f, .35f, .05f, 1f); sh.effectDistance = new Vector2(0, -4 * K);
            h1.gameObject.AddComponent<Outline>().effectColor = new Color(1f, .83f, .3f, .35f);
            var place = UiKit.Txt(c.box, new Vector2(.5f, 1), new Vector2(0, -120), new Vector2(700, 60), 52, TextAnchor.MiddleCenter, Color.white);
            place.rectTransform.pivot = new Vector2(.5f, 1);
            place.text = winner == null ? $"#1 <size={Mathf.RoundToInt(24 * K)}><color=#ffffffcc>/ {players}</color></size>" : winner;
            var sub = UiKit.Txt(c.box, new Vector2(.5f, 1), new Vector2(0, -182), new Vector2(700, 26), 18, TextAnchor.MiddleCenter, Color.white);
            sub.rectTransform.pivot = new Vector2(.5f, 1);
            sub.text = $"Last one standing · {kills} kill{(kills == 1 ? "" : "s")}" + (winner != null && myRank > 0 ? $"   ·   you placed #{myRank} / {players}" : "");
            c.count = UiKit.Txt(c.box, new Vector2(.5f, 1), new Vector2(0, -222), new Vector2(700, 20), 12, TextAnchor.MiddleCenter, new Color(1, 1, 1, .75f));
            c.count.rectTransform.pivot = new Vector2(.5f, 1);
            var lobby = UiKit.Img(c.box, "lobby", UiKit.GoldPlate, new Vector2(.5f, 1), new Vector2(0, -250), new Vector2(150, 36), Color.white, true);
            lobby.rectTransform.pivot = new Vector2(.5f, 1);
            var lt = UiKit.Txt(lobby.rectTransform, new Vector2(.5f, .5f), Vector2.zero, new Vector2(150, 36), 13, TextAnchor.MiddleCenter, new Color(27 / 255f, 20 / 255f, 4 / 255f));
            lt.rectTransform.pivot = new Vector2(.5f, .5f); lt.text = "LOBBY"; Destroy(lt.GetComponent<Shadow>());
            Clickable.Add(lobby.gameObject, c.Leave);
            c.box.localScale = Vector3.one * 0.3f;
            // only the celebration on screen, only the victory music
            if (hudRoot != null) hudRoot.gameObject.SetActive(false);
            AudioListener.pause = true;
            var clip = Resources.Load<AudioClip>("Audio/victory_light"); // light and calm like the lobby music, a tune of its own
            if (clip != null)
            {
                c.music = shade.gameObject.AddComponent<AudioSource>();
                c.music.clip = clip; c.music.loop = true; c.music.ignoreListenerPause = true; c.music.spatialBlend = 0f; c.music.volume = Profile.SoundOn || Profile.MusicOn ? 0.32f : 0f;
                c.music.ignoreListenerVolume = true; c.music.Play();
            }
        }

        bool left;
        void Leave()
        {
            if (left) return; left = true;
            AudioListener.pause = false;
            if (GameBoot.Instance != null) GameBoot.Instance.Restart();
        }

        void Update()
        {
            t += Time.unscaledDeltaTime;
            // pop in (overshoot like the web's cubic-bezier(.2,1.6,.4,1))
            float p = Mathf.Clamp01(t / 0.7f), e = 1f + 2.2f * Mathf.Pow(p - 1f, 3) + 1.2f * Mathf.Pow(p - 1f, 2);
            box.localScale = Vector3.one * Mathf.LerpUnclamped(0.3f, 1f, p >= 1f ? 1f : e);
            var h = ((RectTransform)transform).rect.height;
            for (int i = 0; i < bits.Length; i++)
            {
                float k = (t - bits[i].phase) * bits[i].speed; if (k < 0) continue;
                k -= Mathf.Floor(k);
                bits[i].rt.anchoredPosition = new Vector2(k * bits[i].sway, 20 * UiKit.K - k * (h + 40 * UiKit.K));
                bits[i].rt.localEulerAngles = new Vector3(0, 0, k * 720f + bits[i].spin);
            }
            int secs = Mathf.CeilToInt(Seconds - t);
            count.text = $"Back to the lobby in {Mathf.Max(0, secs)}…";
            if (t >= Seconds) Leave();
        }

        void OnDestroy() { AudioListener.pause = false; }
    }
}
