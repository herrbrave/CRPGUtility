using System.Collections.Generic;
using System.Linq;

namespace VikingRaid.Game
{
    /// <summary>
    /// FULL asset paths of every Synty prefab the world uses (bare names are ambiguous: FX_Fire_01 exists 4 times, SM_Prop_Fire_Pit_01 and SM_Prop_Brazier_01 twice, the
    /// pack rocks/bushes twice). Sizes in the comments were MEASURED by the Task 3 asset probe (<c>Viking Raid/Task 3/Probe Synty Assets</c>), in metres, as width x height x depth.
    /// <c>WorldAssetsTests</c> loads every path, so a renamed or missing prefab is a failing test, never a null at authoring time.
    /// </summary>
    public static class WorldAssets
    {
        private const string Alpine = "Assets/Synty/PolygonNatureBiomes/PNB_Alpine_Mountain/Prefabs/";
        private const string Viking = "Assets/Synty/PolygonVikingRealm/Prefabs/";
        private const string WarCamp = "Assets/Synty/PolygonGoblinWarCamp/Prefabs/";
        private const string Fx = "Assets/Synty/PolygonParticleFX/Prefabs/";

        // ---- forest. The Viking Realm pines are single-mesh, 1-3k tris, no LODGroup: the bulk. The Alpine pines carry LODGroups and are taller: variety near the road.
        public static readonly string[] VikingPines =
        {
            Viking + "Environment/SM_Env_Tree_Pine_01.prefab",   // 3.6 x 11.5 x 3.0
            Viking + "Environment/SM_Env_Tree_Pine_02.prefab",   // 4.5 x 13.8 x 4.2
            Viking + "Environment/SM_Env_Tree_Pine_03.prefab",   // 3.6 x 11.9 x 3.1
            Viking + "Environment/SM_Env_Tree_Pine_04.prefab",   // 4.3 x 10.1 x 3.8
            Viking + "Environment/SM_Env_Tree_Pine_05.prefab",   // 2.8 x 9.7 x 2.6
        };

        public static readonly string[] AlpinePines =
        {
            Alpine + "SM_Env_Pine_01.prefab",                    // 6.5 x 17.0 x 5.1 (LODGroup)
            Alpine + "SM_Env_Pine_02.prefab",                    // 5.2 x 11.6 x 5.6 (LODGroup)
            Alpine + "SM_Env_Pine_NoLeaves_01.prefab",           // 5.4 x 16.3 x 4.5: a dead pine, a few of these break the wall of green
        };

        public static readonly string DeadStump = Alpine + "SM_Env_Pine_Stump_01.prefab";
        public static readonly string[] Bushes = { Alpine + "SM_Env_Bush_01.prefab", Alpine + "SM_Env_Bush_02.prefab" };
        public static readonly string[] Ferns = { WarCamp + "Environment/SM_Env_Fern_01.prefab", WarCamp + "Environment/SM_Env_Fern_02.prefab" };
        public static readonly string[] SmallRocks = { Alpine + "SM_Env_Rock_Small_01.prefab", Alpine + "SM_Env_Rock_Small_02.prefab" };
        public static readonly string[] Boulders = { Alpine + "SM_Env_Rock_01.prefab", Alpine + "SM_Env_Rock_02.prefab", Alpine + "SM_Env_Rock_03.prefab", Alpine + "SM_Env_Rock_04.prefab" };
        public static readonly string[] Cliffs = { Viking + "Environment/SM_Env_Rock_Cliff_01.prefab", Viking + "Environment/SM_Env_Rock_Cliff_02.prefab", Viking + "Environment/SM_Env_Rock_Cliff_03.prefab" };   // the Alpine cliffs render a saturated blue
        public static readonly string[] Mountains = { Alpine + "SM_Env_MountainRange_01.prefab", Alpine + "SM_Env_MountainRange_02.prefab", Alpine + "SM_Env_MountainRange_03.prefab", Alpine + "SM_Env_MountainRange_04.prefab" };
        public static readonly string[] Logs = { Viking + "Props/SM_Prop_Log_01.prefab", Viking + "Props/SM_Prop_Log_02.prefab", WarCamp + "Environment/SM_Env_Log_01.prefab" };

        // ---- camp structures (War Camp)
        public static readonly string[] WallSegments = { WarCamp + "Buildings/SM_Bld_Log_Wall_01.prefab", WarCamp + "Buildings/SM_Bld_Log_Wall_02.prefab" };   // 4.5 x 4.4 x 1.2
        public static readonly string[] WallTall = { WarCamp + "Buildings/SM_Bld_Log_Wall_03.prefab", WarCamp + "Buildings/SM_Bld_Log_Wall_04.prefab" };       // 3.7 x 5.1 x 0.75
        public static readonly string[] Stakes = { WarCamp + "Buildings/SM_Bld_Log_Wall_Stake_01.prefab", WarCamp + "Buildings/SM_Bld_Log_Wall_Stake_02.prefab" };
        public static readonly string Gate = WarCamp + "Buildings/SM_Bld_Gate_01.prefab";                  // 10.2 x 11.9 x 2.7, doors at x=+-3.81 in the CLOSED pose
        public static readonly string TowerBig = WarCamp + "Buildings/SM_Bld_Tower_01.prefab";             // 4.7 x 11.7 x 4.6
        public static readonly string TowerSmall = WarCamp + "Buildings/SM_Bld_Tower_02.prefab";           // 2.7 x 9.0 x 3.2
        public static readonly string Headquarters = WarCamp + "Buildings/SM_Bld_HeadQuarters_01.prefab";  // 40.4 x 24.3 x 23.2 -- placed at half scale
        public static readonly string LeaderTent = WarCamp + "Buildings/SM_Bld_Tent_Leader_01.prefab";     // 7.6 x 8.0 x 8.4
        public static readonly string[] Tents =
        {
            WarCamp + "Buildings/SM_Bld_Tent_Basic_01.prefab",    // 3.1 x 4.6 x 3.3
            WarCamp + "Buildings/SM_Bld_Tent_Basic_02.prefab",    // 3.4 x 4.7 x 5.4
            WarCamp + "Buildings/SM_Bld_Tent_Medium_01.prefab",   // 7.8 x 3.8 x 4.8
            WarCamp + "Buildings/SM_Bld_Tent_Small_01.prefab",    // 5.5 x 7.1 x 5.3
        };

        // ---- camp props
        public static readonly string CampFire = WarCamp + "Props/SM_Prop_Camp_Fire_01.prefab";           // 1.7 x 0.9 x 1.9
        public static readonly string FirePit = WarCamp + "Props/SM_Prop_Fire_Pit_02.prefab";             // 3.0 x 2.2 x 3.1
        public static readonly string Brazier = WarCamp + "Props/SM_Prop_Brazier_01.prefab";              // 1.3 x 0.4 x 1.4
        public static readonly string PoleTorch = WarCamp + "Props/SM_Prop_Torch_01.prefab";              // 0.6 x 6.0 x 0.6
        public static readonly string[] Flags = { WarCamp + "Props/SM_Prop_Flag_01.prefab", WarCamp + "Props/SM_Prop_Flag_02.prefab" };
        public static readonly string[] Spikes = { WarCamp + "Props/SM_Prop_Defence_Spikes_01.prefab", WarCamp + "Props/SM_Prop_Defence_Spikes_02.prefab" };
        public static readonly string[] Bones = { WarCamp + "Environment/SM_Env_Bones_01.prefab", WarCamp + "Environment/SM_Env_Bones_02.prefab" };
        public static readonly string[] Crates = { WarCamp + "Props/SM_Prop_Crate_01.prefab", WarCamp + "Props/SM_Prop_Crate_02.prefab", WarCamp + "Props/SM_Prop_Barrel_01.prefab", WarCamp + "Props/SM_Prop_Barrel_02.prefab" };
        public static readonly string[] Racks = { WarCamp + "Props/SM_Prop_Rack_Weapon_01.prefab", WarCamp + "Props/SM_Prop_Meat_Rack_01.prefab", WarCamp + "Props/SM_Prop_Hide_Rack_01.prefab" };
        public static readonly string[] Trophies = { WarCamp + "Props/SM_Prop_Head_Spike_01.prefab", WarCamp + "Props/SM_Prop_Skull_Pile_01.prefab", WarCamp + "Props/SM_Prop_Gibbet_Cage_01.prefab", WarCamp + "Props/SM_Prop_Effigy_01.prefab" };
        public static readonly string[] Furniture = { WarCamp + "Props/SM_Prop_Table_01.prefab", WarCamp + "Props/SM_Prop_Bench_01.prefab", WarCamp + "Props/SM_Prop_Seat_01.prefab" };

        // ---- effects (Particle FX pack: its bare names are unique there)
        public static readonly string FireBig = Fx + "FX_Fire_Big_01.prefab";
        public static readonly string FireSmall = Fx + "FX_Fire_Small_01.prefab";
        public static readonly string Embers = Fx + "FX_Embers_01.prefab";
        public static readonly string SmokeBlack = Fx + "FX_Smoke_Black_01.prefab";
        public static readonly string FogSmall = Fx + "FX_Fog_Small_01.prefab";
        public static readonly string FogBig = Fx + "FX_Fog_Big_01.prefab";
        public static readonly string DustBlowing = Fx + "FX_Dust_Blowing_Soft_01.prefab";
        public static readonly string Fireflies = Fx + "FX_Fireflies_01.prefab";
        // combat effects (CombatFx)
        public static readonly string Arrow = Viking + "Weapons/SM_Wep_Arrow_01.prefab";
        public static readonly string ImpactSmall = Fx + "FX_Impact_Small_01.prefab";
        public static readonly string ImpactLarge = Fx + "FX_Impact_Large_01.prefab";
        public static readonly string Heal = Fx + "FX_Heal_01.prefab";
        public static readonly string MissDust = Fx + "FX_Impact_Dirt_01.prefab";
        public static readonly string BoltImpact = Fx + "FX_Fire_Explosion_01.prefab";
        public static readonly string Leaves = "Assets/Synty/PolygonNatureBiomes/PNB_Alpine_Mountain/FX/FX_Prefabs/FX_Leaves_01.prefab";

        public static IEnumerable<string> All => typeof(WorldAssets).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(string) || f.FieldType == typeof(string[]))
            .SelectMany(f => f.GetValue(null) is string single ? new[] { single } : (string[])f.GetValue(null));
    }
}
