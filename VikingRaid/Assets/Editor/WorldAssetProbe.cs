using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VikingRaid.EditorTools
{
    /// <summary>
    /// Task 3 spike: prints, for every Synty prefab the world might use, where it lives (and whether the bare name is ambiguous), its combined renderer bounds in the
    /// prefab's own space (size, centre, pivot offset), its colliders and LODs. The authoring script positions walls, tents and gates from these measured sizes rather
    /// than from guesses. Output: VikingRaid_Logs/spikes/world_asset_probe.txt
    /// </summary>
    public static class WorldAssetProbe
    {
        private static readonly string[] Names =
        {
            // forest / terrain dressing
            "SM_Env_Pine_01", "SM_Env_Pine_02", "SM_Env_Pine_03", "SM_Env_Pine_04", "SM_Env_Pine_05", "SM_Env_Pine_NoLeaves_01", "SM_Env_Pine_Stump_01",
            "SM_Env_Tree_Pine_01", "SM_Env_Tree_Pine_02", "SM_Env_Tree_Pine_03", "SM_Env_Tree_Pine_04", "SM_Env_Tree_Pine_05", "SM_Env_Tree_Pine_06",
            "SM_Env_Background_Trees_01", "SM_Env_Background_Trees_02", "SM_Env_MountainRange_01", "SM_Env_MountainRange_02", "SM_Env_MountainRange_03", "SM_Env_MountainRange_04",
            "SM_Env_Rock_01", "SM_Env_Rock_02", "SM_Env_Rock_03", "SM_Env_Rock_04", "SM_Env_Rock_Small_01", "SM_Env_Rock_Small_02", "SM_Env_Rock_Cliff_01", "SM_Env_Rock_Cliff_02",
            "SM_Env_Rock_Cliff_03", "SM_Env_Bush_01", "SM_Env_Bush_02", "SM_Env_Moss_Lumps_01", "SM_Env_GroundCover_01", "SM_Prop_Log_01", "SM_Prop_Log_02", "SM_Env_Log_01",
            "SM_Env_Stump_01", "SM_Env_Fern_01", "SM_Env_Mud_Cliff_01", "SM_Env_Mud_Cliff_02", "SM_Env_Mud_Cliff_03",
            // camp
            "SM_Bld_Log_Wall_01", "SM_Bld_Log_Wall_02", "SM_Bld_Log_Wall_03", "SM_Bld_Log_Wall_04", "SM_Bld_Log_Wall_05", "SM_Bld_Log_Wall_Stake_01", "SM_Bld_Log_Wall_Stake_02",
            "SM_Bld_Gate_01", "SM_Bld_Gate_02", "SM_Bld_Tower_01", "SM_Bld_Tower_02", "SM_Bld_HeadQuarters_01", "SM_Bld_Tent_Leader_01", "SM_Bld_Tent_Leader_02",
            "SM_Bld_Tent_Basic_01", "SM_Bld_Tent_Basic_02", "SM_Bld_Tent_Medium_01", "SM_Bld_Tent_Medium_02", "SM_Bld_Tent_Small_01", "SM_Bld_Tent_Small_02", "SM_Bld_Shaman_01",
            "SM_Bld_Workshop_01", "SM_Bld_Mess_Hall_01", "SM_Bld_Barracks_01", "SM_Bld_Pole_Cloth_01", "SM_Bld_Pole_Spike_01",
            "SM_Prop_Camp_Fire_01", "SM_Prop_Fire_Pit_01", "SM_Prop_Fire_Pit_02", "SM_Prop_Brazier_01", "SM_Prop_Torch_01", "SM_Prop_Torch_02", "SM_Prop_Torch_03",
            "SM_Prop_Flag_01", "SM_Prop_Flag_02", "SM_Prop_Defence_Spikes_01", "SM_Prop_Defence_Spikes_02", "SM_Env_Bones_01", "SM_Env_Bones_02", "SM_Prop_Skull_Pile_01",
            "SM_Prop_Skull_Pile_02", "SM_Prop_Crate_01", "SM_Prop_Crate_02", "SM_Prop_Barrel_01", "SM_Prop_Barrel_02", "SM_Prop_Gibbet_Cage_01", "SM_Prop_Rack_Weapon_01",
            "SM_Prop_Meat_Rack_01", "SM_Prop_Effigy_01", "SM_Prop_Head_Spike_01", "SM_Prop_Table_01", "SM_Prop_Bench_01", "SM_Prop_Seat_01", "SM_Prop_Banner_01",
            "SM_Prop_Sack_Stack_01", "SM_Prop_Spit_01", "SM_Prop_Hide_Rack_01", "SM_Prop_Shield_Hanging_01", "SM_Prop_Weapon_Hanging_01", "SM_Prop_Bedroll_01", "SM_Prop_Cauldron_01",
            "SM_Prop_Pot_01", "SM_Prop_Idol_01", "SM_Prop_Shrine_01", "SM_Prop_Archway_01",
            // fx
            "SM_Wep_Arrow_01", "FX_Impact_Small_01", "FX_Heal_01", "FX_Fire_01", "FX_Fire_Big_01", "FX_Fire_Small_01", "FX_Embers_01", "FX_Smoke_Black_01", "FX_Smoke_Large_01", "FX_Fog_Small_01", "FX_Fog_Big_01", "FX_Fog_01",
            "FX_Dust_Blowing_Soft_01", "FX_Leaves_01", "FX_Fireflies_01", "FX_Flies_01", "FX_Sunray_01", "FX_GlowSpot_01",
        };

        [MenuItem("Viking Raid/Task 3/Probe Synty Assets")]
        public static void Run()
        {
            var report = new StringBuilder();
            foreach (var name in Names)
            {
                var matches = AssetDatabase.FindAssets($"{name} t:Prefab", new[] { "Assets/Synty" })
                    .Select(AssetDatabase.GUIDToAssetPath).Where(p => Path.GetFileNameWithoutExtension(p) == name).ToList();
                if (matches.Count == 0) { report.AppendLine($"MISSING {name}"); continue; }
                foreach (var path in matches)
                {
                    var root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        var renderers = root.GetComponentsInChildren<Renderer>(true).Where(r => r is not ParticleSystemRenderer).ToList();
                        var colliders = root.GetComponentsInChildren<Collider>(true).Select(c => c.GetType().Name.Replace("Collider", "") + (c is MeshCollider m ? (m.convex ? "(convex)" : "(mesh)") : "")).ToList();
                        var lods = root.GetComponentsInChildren<LODGroup>(true).Length;
                        var tris = root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null && (f.GetComponent<Renderer>()?.enabled ?? true)).Sum(f => f.sharedMesh.triangles.Length / 3);
                        var particles = root.GetComponentsInChildren<ParticleSystem>(true).Length;
                        var lights = root.GetComponentsInChildren<Light>(true).Length;
                        string bounds = "no renderers";
                        if (renderers.Count > 0)
                        {
                            var b = renderers[0].bounds;
                            foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
                            bounds = $"size=({b.size.x:F2},{b.size.y:F2},{b.size.z:F2}) centre=({b.center.x:F2},{b.center.y:F2},{b.center.z:F2}) minY={b.min.y:F2}";
                        }

                        var dup = matches.Count > 1 ? $" [DUP x{matches.Count}]" : "";
                        report.AppendLine($"{name}{dup}\n   {path}\n   {bounds} | colliders: {(colliders.Count == 0 ? "none" : string.Join(",", colliders))} | LODGroups={lods} tris={tris} particles={particles} lights={lights} children={root.transform.childCount}");
                        if (name == "SM_Bld_Gate_01" || name == "SM_Env_Pine_01")
                            foreach (var t in root.GetComponentsInChildren<Transform>(true).Skip(1).Take(12))
                                report.AppendLine($"      child {t.name} pos=({t.localPosition.x:F2},{t.localPosition.y:F2},{t.localPosition.z:F2}) rot=({t.localEulerAngles.x:F0},{t.localEulerAngles.y:F0},{t.localEulerAngles.z:F0}) collider={t.GetComponent<Collider>()?.GetType().Name}");
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
            }

            var dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "world_asset_probe.txt"), report.ToString());
            Debug.Log($"[WorldAssetProbe] wrote {Path.Combine(dir, "world_asset_probe.txt")} ({Names.Length} names)");
        }
    }
}
