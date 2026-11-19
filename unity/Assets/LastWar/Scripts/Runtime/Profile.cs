using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace LastWar
{
    /// <summary>Saved player profile + settings (PlayerPrefs), same fields as the web lobby.</summary>
    public static class Profile
    {
        public static string Name { get => PlayerPrefs.GetString("lw.name", "Soldier"); set { PlayerPrefs.SetString("lw.name", string.IsNullOrWhiteSpace(value) ? "Soldier" : value.Trim()); PlayerPrefs.Save(); } }
        public static int Skin { get => Mathf.Clamp(PlayerPrefs.GetInt("lw.skin2", 0), 0, SoldierBody.OutfitFiles.Length - 1); set { PlayerPrefs.SetInt("lw.skin2", value); PlayerPrefs.Save(); } }
        /// <summary>mix and match: the outfit worn for each piece (0 head, 1 torso, 2 legs, 3 feet); -1 = the full outfit's</summary>
        public static int Part(int i) => -1; // (outfits are worn whole)
        public static void SetPart(int i, int skin) { PlayerPrefs.SetInt("lw.part" + i, skin); PlayerPrefs.Save(); }
        public static int[] Parts => null;
        public static void ClearParts() { for (int i = 0; i < 4; i++) PlayerPrefs.DeleteKey("lw.part" + i); PlayerPrefs.Save(); }
        public static string Map { get => PlayerPrefs.GetString("lw.map", "green"); set { PlayerPrefs.SetString("lw.map", value); PlayerPrefs.Save(); } }
        /// <summary>a new map after every match (rotation); false = the chosen one stays</summary>
        public static bool MapAuto { get => PlayerPrefs.GetInt("lw.mapAuto", 1) == 1; set { PlayerPrefs.SetInt("lw.mapAuto", value ? 1 : 0); PlayerPrefs.Save(); } }
        public static bool AutoPickup { get => PlayerPrefs.GetInt("lw.autoPick", 1) == 1; set { PlayerPrefs.SetInt("lw.autoPick", value ? 1 : 0); PlayerPrefs.Save(); } }
        public static bool MusicOn { get => PlayerPrefs.GetInt("lw.music", 1) == 1; set { PlayerPrefs.SetInt("lw.music", value ? 1 : 0); PlayerPrefs.Save(); } }
        public static bool SoundOn { get => PlayerPrefs.GetInt("lw.sound", 1) == 1; set { PlayerPrefs.SetInt("lw.sound", value ? 1 : 0); PlayerPrefs.Save(); AudioListener.volume = value ? 1f : 0f; } }
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
            Armory.Coins += Armory.Reward(kills, win, place);
            PlayerPrefs.SetInt("lw.xp", Xp + 10 + kills * 5 + (win ? 30 : Mathf.Max(0, 16 - place)));
            PlayerPrefs.Save();
        }

        // ---- settings
        public static float Sensitivity { get => PlayerPrefs.GetFloat("lw.sens", 1f); set { PlayerPrefs.SetFloat("lw.sens", value); PlayerPrefs.Save(); } }
        public static float ScopeSensitivity { get => PlayerPrefs.GetFloat("lw.scopeSens", 1f); set { PlayerPrefs.SetFloat("lw.scopeSens", value); PlayerPrefs.Save(); } }
        public static bool AimAssist { get => PlayerPrefs.GetInt("lw.assist", 1) == 1; set { PlayerPrefs.SetInt("lw.assist", value ? 1 : 0); PlayerPrefs.Save(); } }
        /// <summary>0 low, 1 medium, 2 high</summary>
        public static int Quality { get => PlayerPrefs.GetInt("lw.quality", 2); set { PlayerPrefs.SetInt("lw.quality", value); PlayerPrefs.Save(); ApplyQuality(); } }

        /// <summary>frames per second: 60 (default), 90 or 120 - a 90 / 120 Hz screen shows a smoother picture and aim at the
        /// cost of battery and heat</summary>
        public static int Fps { get => PlayerPrefs.GetInt("lw.fps", 60); set { PlayerPrefs.SetInt("lw.fps", value); PlayerPrefs.Save(); Application.targetFrameRate = value; } }

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
            AudioListener.volume = SoundOn ? 1f : 0f;
        }
    }
}
