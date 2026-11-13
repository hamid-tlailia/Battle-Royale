using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace LastWar
{
    /// <summary>Saved player profile + settings (PlayerPrefs), same fields as the web lobby.</summary>
    public static class Profile
    {
        public static string Name { get => PlayerPrefs.GetString("lw.name", "Soldier"); set { PlayerPrefs.SetString("lw.name", string.IsNullOrWhiteSpace(value) ? "Soldier" : value.Trim()); PlayerPrefs.Save(); } }
        public static int Skin { get => PlayerPrefs.GetInt("lw.skin", 0); set { PlayerPrefs.SetInt("lw.skin", value); PlayerPrefs.Save(); } }
        public static string Map { get => PlayerPrefs.GetString("lw.map", "green"); set { PlayerPrefs.SetString("lw.map", value); PlayerPrefs.Save(); } }
        public static int Wins => PlayerPrefs.GetInt("lw.wins", 0);
        public static int Kills => PlayerPrefs.GetInt("lw.kills", 0);
        public static int Matches => PlayerPrefs.GetInt("lw.matches", 0);
        public static int Xp => PlayerPrefs.GetInt("lw.xp", 0);
        /// <summary>level from xp: 60 xp for level 2, then +20 per level (the web game's curve, lowered)</summary>
        public static int Level { get { int xp = Xp, lv = 1, need = 60; while (xp >= need) { xp -= need; lv++; need += 20; } return lv; } }
        public static (int have, int need) LevelProgress { get { int xp = Xp, need = 60; while (xp >= need) { xp -= need; need += 20; } return (xp, need); } }

        public static void AddMatch(int kills, bool win, int place)
        {
            PlayerPrefs.SetInt("lw.matches", Matches + 1);
            PlayerPrefs.SetInt("lw.kills", Kills + kills);
            if (win) PlayerPrefs.SetInt("lw.wins", Wins + 1);
            PlayerPrefs.SetInt("lw.xp", Xp + 10 + kills * 5 + (win ? 30 : Mathf.Max(0, 16 - place)));
            PlayerPrefs.Save();
        }

        // ---- settings
        public static float Sensitivity { get => PlayerPrefs.GetFloat("lw.sens", 1f); set { PlayerPrefs.SetFloat("lw.sens", value); PlayerPrefs.Save(); } }
        public static bool AimAssist { get => PlayerPrefs.GetInt("lw.assist", 1) == 1; set { PlayerPrefs.SetInt("lw.assist", value ? 1 : 0); PlayerPrefs.Save(); } }
        /// <summary>0 low, 1 medium, 2 high</summary>
        public static int Quality { get => PlayerPrefs.GetInt("lw.quality", 2); set { PlayerPrefs.SetInt("lw.quality", value); PlayerPrefs.Save(); ApplyQuality(); } }

        public static void ApplyQuality()
        {
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                int q = Quality;
                urp.renderScale = q == 0 ? 0.7f : q == 1 ? 0.85f : 1f;
                urp.msaaSampleCount = q == 0 ? 1 : q == 1 ? 2 : 4;
                urp.shadowDistance = q == 0 ? 40f : q == 1 ? 70f : 90f;
            }
            PlayerController.AimAssistOn = AimAssist;
        }
    }
}
