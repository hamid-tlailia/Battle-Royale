using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace LastWar
{
    /// <summary>
    /// In-match HUD: health / armour, weapon slots with ammo, alive + kills, zone timer, crosshair dot that turns
    /// red over an enemy, hit marker (red on headshots), kill feed, damage flash, skull banner on kills, end screen.
    /// </summary>
    public class Hud : MonoBehaviour
    {
        TouchControls input; PlayerController player;
        Image hpFill, armorFill, dot, flash;
        Text hpText, aliveText, zoneText, feedText, bannerText, centerText;
        RectTransform endPanel; Text endTitle, endSub;
        readonly List<string> feed = new List<string>(); readonly List<float> feedTime = new List<float>();
        float hitT, flashT, bannerT; bool hitHead;
        static Font font;

        public static Hud Create(TouchControls input, PlayerController player)
        {
            var h = new GameObject("Hud").AddComponent<Hud>();
            h.input = input; h.player = player; h.Build();
            Fighter.OnHit += h.Hit; Fighter.OnKill += h.Kill;
            return h;
        }

        RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform; rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor; rt.anchoredPosition = pos; rt.sizeDelta = size;
            var img = go.GetComponent<Image>(); img.color = color; img.raycastTarget = false;
            return rt;
        }

        Text Txt(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, int fs, TextAnchor align)
        {
            var t = new GameObject("text", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            var rt = (RectTransform)t.transform; rt.SetParent(parent, false);
            rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor; rt.anchoredPosition = pos; rt.sizeDelta = size;
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.font = font; t.fontSize = fs; t.alignment = align; t.color = Color.white; t.fontStyle = FontStyle.Bold; t.raycastTarget = false;
            var sh = t.gameObject.AddComponent<Shadow>(); sh.effectColor = new Color(0, 0, 0, 0.7f); sh.effectDistance = new Vector2(2, -2);
            return t;
        }

        void Build()
        {
            Transform c = input.Canvas.transform;
            // damage flash behind everything
            flash = Rect("Flash", c, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4000, 4000), new Color(0.8f, 0, 0, 0)).GetComponent<Image>();
            flash.transform.SetAsFirstSibling();

            // health + armour bars, bottom centre above the weapon slots
            RectTransform hpBg = Rect("HpBg", c, new Vector2(0.5f, 0), new Vector2(0, 132), new Vector2(520, 22), new Color(0, 0, 0, 0.5f));
            hpFill = Rect("Hp", hpBg, new Vector2(0, 0.5f), Vector2.zero, new Vector2(520, 22), new Color(0.92f, 0.95f, 0.92f, 0.95f)).GetComponent<Image>();
            RectTransform arBg = Rect("ArBg", c, new Vector2(0.5f, 0), new Vector2(0, 158), new Vector2(520, 10), new Color(0, 0, 0, 0.45f));
            armorFill = Rect("Ar", arBg, new Vector2(0, 0.5f), Vector2.zero, new Vector2(520, 10), new Color(0.35f, 0.7f, 1f, 0.95f)).GetComponent<Image>();
            hpText = Txt(c, new Vector2(0.5f, 0), new Vector2(0, 172), new Vector2(300, 34), 24, TextAnchor.MiddleCenter);

            aliveText = Txt(c, new Vector2(1, 1), new Vector2(-30, -20), new Vector2(520, 44), 32, TextAnchor.UpperRight);
            zoneText = Txt(c, new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(800, 40), 28, TextAnchor.UpperCenter);
            feedText = Txt(c, new Vector2(1, 1), new Vector2(-30, -80), new Vector2(620, 220), 24, TextAnchor.UpperRight);
            bannerText = Txt(c, new Vector2(0.5f, 1), new Vector2(0, -90), new Vector2(900, 70), 44, TextAnchor.MiddleCenter);
            centerText = Txt(c, new Vector2(0.5f, 0.5f), new Vector2(0, 180), new Vector2(1000, 60), 34, TextAnchor.MiddleCenter);

            // crosshair: a dot, plus an X hit marker around it
            dot = Rect("Dot", c, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10, 10), Color.white).GetComponent<Image>();
            dot.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            hitMarkText = Txt(c, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(90, 90), 56, TextAnchor.MiddleCenter);
            hitMarkText.rectTransform.pivot = new Vector2(0.5f, 0.5f); hitMarkText.text = "X"; hitMarkText.color = new Color(1, 1, 1, 0);

            // end screen
            endPanel = Rect("End", c, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4000, 4000), new Color(0, 0, 0, 0.6f));
            endPanel.GetComponent<Image>().raycastTarget = true;
            endTitle = Txt(endPanel, new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(1200, 120), 96, TextAnchor.MiddleCenter);
            endSub = Txt(endPanel, new Vector2(0.5f, 0.5f), new Vector2(0, 20), new Vector2(1200, 60), 36, TextAnchor.MiddleCenter);
            RectTransform again = Rect("Again", endPanel, new Vector2(0.5f, 0.5f), new Vector2(0, -110), new Vector2(360, 100), new Color(0.85f, 0.55f, 0.1f, 0.95f));
            again.pivot = new Vector2(0.5f, 0.5f);
            again.GetComponent<Image>().raycastTarget = true;
            Text at = Txt(again, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(360, 100), 40, TextAnchor.MiddleCenter); at.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            at.text = "PLAY AGAIN";
            again.gameObject.AddComponent<Button>().onClick.AddListener(() => GameBoot.Instance.Restart());
            endPanel.gameObject.SetActive(false);
        }

        Text hitMarkText;

        void Hit(Fighter victim, Fighter attacker, float dmg, bool head, Vector3 point)
        {
            if (attacker != null && attacker == player.fighter && victim != attacker) { hitT = 0.25f; hitHead = head; }
            if (victim == player.fighter) flashT = Mathf.Min(0.5f, flashT + dmg / 60f);
        }

        void Kill(Fighter victim, Fighter killer, string weapon, bool head)
        {
            string w = weapon == "zone" ? "the zone" : (Tuning.Weapon(weapon) != null ? Tuning.Weapon(weapon).name : weapon);
            string line = killer != null && killer != victim ? $"{killer.displayName}  [{w}{(head ? " HEADSHOT" : "")}]  {victim.displayName}" : $"{victim.displayName} was killed by {w}";
            feed.Insert(0, line); feedTime.Insert(0, Time.time);
            if (feed.Count > 5) { feed.RemoveAt(5); feedTime.RemoveAt(5); }
            if (killer == player.fighter && victim != killer) { bannerT = 2.2f; bannerText.text = (head ? "HEADSHOT  ·  " : "") + "ELIMINATED " + victim.displayName.ToUpper(); }
        }

        void Update()
        {
            Fighter f = player.fighter; float dt = Time.deltaTime;
            hpFill.rectTransform.sizeDelta = new Vector2(520 * f.hp / Fighter.MaxHp, 22);
            hpFill.color = f.hp > 35 ? new Color(0.92f, 0.95f, 0.92f, 0.95f) : new Color(1f, 0.3f, 0.25f, 0.95f);
            armorFill.rectTransform.sizeDelta = new Vector2(520 * f.armor / 100f, 10);
            hpText.text = Mathf.CeilToInt(f.hp).ToString();

            GameBoot g = GameBoot.Instance;
            aliveText.text = $"ALIVE {g.AliveCount}    KILLS {f.kills}";

            Zone z = Zone.Instance;
            if (z != null)
            {
                int s = Mathf.CeilToInt(z.SecondsLeft); string tm = $"{s / 60}:{s % 60:00}";
                if (!z.Active) zoneText.text = $"ZONE APPEARS IN {tm}";
                else if (z.PhaseEnds > 1e30f) zoneText.text = "FINAL ZONE";
                else zoneText.text = z.Shrinking ? $"ZONE SHRINKING  {tm}" : $"ZONE SHRINKS IN {tm}";
                bool outside = z.Active && z.Outside(player.transform.position);
                zoneText.color = outside ? new Color(1f, 0.45f, 0.35f) : Color.white;
                centerText.text = outside && f.Alive ? "YOU ARE OUTSIDE THE ZONE" : "";
                centerText.color = new Color(1f, 0.4f, 0.3f);
            }

            // weapon slots
            for (int k = 0; k < 3; k++)
            {
                GunState gs = f.guns[k];
                string ammo = gs == null ? "" : (k == f.slot && f.reloadEnd > 0 ? "RELOADING" : $"{gs.mag} / {gs.reserve}");
                input.SlotLabels[k].text = gs == null ? Tuning.SlotNames[k] : $"{gs.def.name.ToUpper()}\n<size=22>{ammo}</size>";
                input.SlotFrames[k].color = k == f.slot ? new Color(0.85f, 0.55f, 0.1f, 0.55f) : new Color(0.05f, 0.07f, 0.09f, 0.45f);
            }
            input.ShowScope(f.Gun != null && f.Gun.def.scopeFov > 0 && f.Gun.def.scopeFov < 40f);

            // crosshair dot red when it is on an enemy
            bool onEnemy = false;
            Camera cam = player.cam;
            if (cam != null && Physics.Raycast(cam.ViewportPointToRay(new Vector3(0.5f, 0.5f)), out RaycastHit h, 250f, ~0, QueryTriggerInteraction.Ignore))
            {
                Fighter t = h.collider.GetComponentInParent<Fighter>();
                onEnemy = t != null && t != f && t.Alive;
            }
            dot.color = onEnemy ? new Color(1f, 0.15f, 0.1f) : Color.white;
            dot.enabled = f.Alive;

            hitT -= dt; hitMarkText.color = hitHead ? new Color(1f, 0.15f, 0.1f, Mathf.Clamp01(hitT * 5f)) : new Color(1, 1, 1, Mathf.Clamp01(hitT * 5f));
            flashT = Mathf.Max(0, flashT - dt); flash.color = new Color(0.8f, 0f, 0f, flashT * 0.6f);
            bannerT -= dt; bannerText.color = new Color(1f, 0.25f, 0.2f, Mathf.Clamp01(bannerT * 2f));

            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < feed.Count; k++) if (Time.time - feedTime[k] < 8f) sb.AppendLine(feed[k]);
            feedText.text = sb.ToString();

            // end of the match
            if (g.Result != 0 && !endPanel.gameObject.activeSelf)
            {
                endPanel.gameObject.SetActive(true); endPanel.SetAsLastSibling();
                endTitle.text = g.Result > 0 ? "VICTORY" : "ELIMINATED";
                endTitle.color = g.Result > 0 ? new Color(1f, 0.8f, 0.25f) : new Color(1f, 0.35f, 0.3f);
                endSub.text = $"PLACE #{g.Rank}   ·   {f.kills} KILLS";
            }
        }
    }
}
