using System.Collections.Generic;
using UnityEngine;

namespace LastWar
{
    /// <summary>
    /// Runtime humanoid avatars for the mixamo rigs (soldier + outfits), so poses transfer through Unity's muscle
    /// space (HumanPoseHandler): lying, crawling, falling and dying work even though the rigs' rest axes differ.
    /// </summary>
    public static class HumanRig
    {
        // Unity human bone -> mixamo bone name (without the "mixamorig:" prefix)
        static readonly (string human, string mixamo)[] Map = {
            ("Hips", "Hips"), ("Spine", "Spine"), ("Chest", "Spine1"), ("UpperChest", "Spine2"), ("Neck", "Neck"), ("Head", "Head"),
            ("LeftShoulder", "LeftShoulder"), ("LeftUpperArm", "LeftArm"), ("LeftLowerArm", "LeftForeArm"), ("LeftHand", "LeftHand"),
            ("RightShoulder", "RightShoulder"), ("RightUpperArm", "RightArm"), ("RightLowerArm", "RightForeArm"), ("RightHand", "RightHand"),
            ("LeftUpperLeg", "LeftUpLeg"), ("LeftLowerLeg", "LeftLeg"), ("LeftFoot", "LeftFoot"), ("LeftToes", "LeftToeBase"),
            ("RightUpperLeg", "RightUpLeg"), ("RightLowerLeg", "RightLeg"), ("RightFoot", "RightFoot"), ("RightToes", "RightToeBase"),
        };

        static string Key(string n) { int c = n.LastIndexOf(':'); return c >= 0 ? n.Substring(c + 1) : n; }

        /// <summary>build a humanoid avatar for the rig under root (in its current = rest pose); null if it is not a usable mixamo rig</summary>
        public static Avatar Build(Transform root)
        {
            // the avatar builder needs unique transform names
            var seen = new HashSet<string>(); int n = 0;
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                if (!seen.Add(t.name) && !t.name.Contains("mixamorig")) { t.name = t.name + "_" + (n++); seen.Add(t.name); } // (bones keep their names: the clips bind by path)
            var byKey = new Dictionary<string, Transform>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) if (t.name.Contains("mixamorig")) byKey[Key(t.name)] = t;
            var human = new List<HumanBone>();
            foreach (var (h, m) in Map)
            {
                if (!byKey.TryGetValue(m, out Transform t)) { if (h == "UpperChest" || h == "LeftToes" || h == "RightToes" || h == "Neck") continue; return null; }
                var hb = new HumanBone { humanName = h, boneName = t.name }; hb.limit.useDefaultValues = true;
                human.Add(hb);
            }
            var skel = new List<SkeletonBone>();
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                skel.Add(new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale });
            var desc = new HumanDescription
            {
                human = human.ToArray(), skeleton = skel.ToArray(),
                upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false,
            };
            Avatar a = AvatarBuilder.BuildHumanAvatar(root.gameObject, desc);
            return a != null && a.isValid && a.isHuman ? a : null;
        }
    }
}
