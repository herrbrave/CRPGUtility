using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Editor.Character;
using UnityEditor;
using UnityEngine;
using VikingRaid.Appearance;
using VikingRaid.Appearance.Editor;
using VikingRaid.EditorTools.Spikes;
using VikingRaid.Game;

namespace VikingRaid.EditorTools
{
    /// <summary>
    /// Task 1: authors the two animation profiles and every armed character prefab of the game (4 heroes, 10 goblin variants), one
    /// re-runnable script (menu <c>Viking Raid/Task 1/Author Characters</c>). Each prefab is a toolkit wrapper built by
    /// <see cref="CharacterWrapperBuilder"/> (CharacterRoot / VisualRoot / Model), then patched: Synty Animators get no root motion and
    /// AlwaysAnimate; weapons are static clones parented to the hand bone (or the Sidekick prop socket) with grips chosen from
    /// <c>GripLab</c> renders; <see cref="ModelActionCues"/> is added to CharacterRoot. A rebuild replaces the Model child (and the weapons
    /// with it) and re-adds both, so running it twice is safe.
    /// <para>Animation: heroes use the Quaternius UAL's neutral humanoid locomotion, goblins the Synty goblin pack (a goblin hunch looks wrong
    /// on a Viking); combat cues come from the UAL for everyone. Reference speeds are MEASURED (<c>LocomotionSpeeds</c>: planted-foot method,
    /// validated within 7% against root-motion data): UAL Jog 4.83 m/s, goblin Run ~3.0 m/s.</para>
    /// </summary>
    public static class CharacterRosterAuthor
    {
        public const string Root = "Assets/Characters";
        public const string PrefabFolder = Root + "/Prefabs";
        public const string VillagerFolder = Root + "/Villagers";   // separate from the combat roster: RosterPrefabTests scans PrefabFolder
        public const string AnimationFolder = Root + "/Animation";
        public const string HeroProfilePath = AnimationFolder + "/HeroAnimationProfile.asset";
        public const string GoblinProfilePath = AnimationFolder + "/GoblinAnimationProfile.asset";
        public const string HeroBowProfilePath = AnimationFolder + "/HeroBowAnimationProfile.asset";
        public const string GoblinBowProfilePath = AnimationFolder + "/GoblinBowAnimationProfile.asset";
        public const string HitPointResourceId = "resource-hp";

        private const string Ual = "Assets/ThirdParty/UAL/UAL1_Standard.fbx";
        private const string GoblinLoco = "Assets/Synty/AnimationGoblinLocomotion/Animations/Polygon/Neutral";
        private const float HeroMoveReferenceSpeed = 4.83f; // UAL Jog_Fwd_Loop, root-motion averageSpeed
        private const float GoblinMoveReferenceSpeed = 3.0f; // Goblin Run_F, planted-foot measurement (3.18 / 2.79)

        private const string V = "Assets/Synty/PolygonVikingRealm/Prefabs";
        private const string W = "Assets/Synty/PolygonGoblinWarCamp/Prefabs";
        private const string S = "Assets/Synty/SidekickCharacters/Characters";

        // Grips from GripLab (the euler is the weapon's local rotation under the hand bone / socket).
        private static readonly Vector3 PolyRightZLong = new(0f, 270f, 0f);   // Viking swords: blade up beside the body
        private static readonly Vector3 PolyRightYLong = new(0f, 0f, 90f);    // axes, clubs, staffs: shaft vertical, head up and OUTWARD (180/270 hid axe heads behind the hood)
        private static readonly Vector3 PolyLeftShield = new(0f, 0f, 90f);    // boss faces forward
        private static readonly Vector3 PolyLeftBow = new(13f, 106f, 97f);      // vertical at FULL DRAW (calibrated by BowAnimationTests from the Bow_Draw hold pose), limbs toward the archer
        private static readonly Vector3 SidekickRight = new(0f, 0f, 90f);     // prop_r: blade hangs at the side
        private static readonly Vector3 SidekickLeftShield = Vector3.zero;    // prop_l

        internal enum Hand { Right, Left }

        internal sealed class Weapon
        {
            public string Prefab;
            public Hand Hand;
            public Vector3 Euler;
            public float Scale = 1f;
            public float Twist; // extra degrees about the hand-space X axis (the shaft direction for the Polygon rigs): turns an axe blade to face the camera
            public Vector3 Offset; // local position under the hand/socket (solved shields are centred on the forearm)
            public string Socket; // optional transform name under the hand bone (Sidekick prop_r / prop_l)
        }

        internal sealed class Entry
        {
            public string Id, Model;
            public bool Hero;
            public Weapon[] Weapons;
            public bool Bow; // archer: uses the Blender-authored Bow_Draw (and, for heroes, Bow_Idle)
            public bool AppearanceBase; // gets a ModelAppearanceBinder wired to the appearance catalog
            public System.Func<ModelAppearanceCatalog> Catalog; // which catalog (null: the Viking one)
            public (float idle, float moving)? Yaw; // measured yaw offsets, overriding the guess from the model path
            public bool BakeDefaultLook; // dress the default look into the prefab so it is visible in the Scene and Prefab views (a skeleton-only master is invisible)
            public float CapsuleHeight = 1.8f, CapsuleRadius = 0.3f, AgentRadius = 0.35f;
        }

        private static Weapon R(string prefab, Vector3 euler, float scale = 1f, string socket = null, float twist = 0f) =>
            new() { Prefab = prefab, Hand = Hand.Right, Euler = euler, Scale = scale, Socket = socket, Twist = twist };

        private static Weapon L(string prefab, Vector3 euler, float scale = 1f, string socket = null) =>
            new() { Prefab = prefab, Hand = Hand.Left, Euler = euler, Scale = scale, Socket = socket };

        internal static readonly Entry[] Roster =
        {
            // ---- heroes (Viking Realm warriors) ----
            new() { Id = "Hero_Bjorn", Hero = true, Model = $"{V}/Characters/SM_Chr_Warrior_Male_02.prefab",
                Weapons = new[] { R($"{V}/Weapons/SM_Wep_Sword_02.prefab", PolyRightZLong, 0.75f), L($"{V}/Weapons/SM_Wep_Shield_01.prefab", PolyLeftShield, 0.7f) } },
            new() { Id = "Hero_Ulf", Hero = true, Model = $"{V}/Characters/SM_Chr_Warrior_Male_01.prefab",
                Weapons = new[] { R($"{V}/Weapons/SM_Wep_Axe_02.prefab", PolyRightYLong, twist: 90f) } },
            new() { Id = "Hero_Sigrid", Hero = true, Bow = true, Model = $"{V}/Characters/SM_Chr_Warrior_Female_01.prefab",
                Weapons = new[] { L($"{V}/Weapons/SM_Wep_Bow_Rigged_01.prefab", PolyLeftBow, 0.9f) } },
            new() { Id = "Hero_Astrid", Hero = true, Model = $"{V}/Characters/SM_Chr_Warrior_Female_02.prefab",
                Weapons = new[] { R($"{V}/Weapons/SM_Wep_Spear_02.prefab", PolyRightYLong, 0.8f) } },

            // ---- goblin grunts (Sidekick species, bare) ----
            new() { Id = "Goblin_Grunt_A", Model = $"{S}/GoblinSpecies/GoblinSpecies_01/GoblinSpecies_01.prefab",
                Weapons = new[] { R($"{W}/Weapons/SM_Wep_Club_01.prefab", SidekickRight, 1f, "prop_r") } },
            new() { Id = "Goblin_Grunt_B", Model = $"{S}/GoblinSpecies/GoblinSpecies_03/GoblinSpecies_03.prefab",
                Weapons = new[] { R($"{W}/Weapons/SM_Wep_Dagger_01.prefab", SidekickRight, 1f, "prop_r") } },
            new() { Id = "Goblin_Grunt_C", Model = $"{S}/GoblinSpecies/GoblinSpecies_05/GoblinSpecies_05.prefab",
                Weapons = new[] { R($"{W}/Weapons/SM_Wep_Cleaver_01.prefab", SidekickRight, 1f, "prop_r") } },

            // ---- goblin warriors (Sidekick fighters, armoured) ----
            new() { Id = "Goblin_Warrior_A", Model = $"{S}/GoblinFighters/GoblinFighter_01/GoblinFighter_01.prefab",
                Weapons = new[] { R($"{W}/Weapons/SM_Wep_Sword_01.prefab", SidekickRight, 1f, "prop_r"), L($"{W}/Weapons/SM_Wep_Shield_01.prefab", SidekickLeftShield, 0.8f, "prop_l") } },
            new() { Id = "Goblin_Warrior_B", Model = $"{S}/GoblinFighters/GoblinFighter_02/GoblinFighter_02.prefab",
                Weapons = new[] { R($"{W}/Weapons/SM_Wep_Cleaver_02.prefab", SidekickRight, 1f, "prop_r"), L($"{W}/Weapons/SM_Wep_Shield_02.prefab", SidekickLeftShield, 0.8f, "prop_l") } },
            new() { Id = "Goblin_Warrior_C", Model = $"{S}/GoblinFighters/GoblinFighter_04/GoblinFighter_04.prefab",
                Weapons = new[] { R($"{W}/Weapons/SM_Wep_Axe_01.prefab", SidekickRight, 1f, "prop_r"), L($"{W}/Weapons/SM_Wep_Shield_03.prefab", SidekickLeftShield, 0.8f, "prop_l") } },

            // ---- War Camp goblins ----
            new() { Id = "Goblin_Archer_M", Bow = true, Model = $"{W}/Characters/SM_Chr_Archer_Male_01.prefab",
                Weapons = new[] { L($"{W}/Weapons/SM_Wep_Bow_Rigged_01.prefab", PolyLeftBow, 0.9f) } },
            new() { Id = "Goblin_Archer_F", Bow = true, Model = $"{W}/Characters/SM_Chr_Archer_Female_01.prefab",
                Weapons = new[] { L($"{W}/Weapons/SM_Wep_Bow_Rigged_01.prefab", PolyLeftBow, 0.9f) } },
            new() { Id = "Goblin_Shaman", Model = $"{W}/Characters/SM_Chr_Shaman_01.prefab",
                Weapons = new[] { R($"{W}/Weapons/SM_Wep_Staff_01.prefab", PolyRightYLong, 0.9f) } },
            new() { Id = "Goblin_Troll", Model = $"{W}/Characters/SM_Chr_Troll_01.prefab", CapsuleHeight = 2.2f, CapsuleRadius = 0.5f, AgentRadius = 0.55f,
                Weapons = new[] { R($"{W}/Weapons/SM_Wep_Club_04.prefab", PolyRightYLong) } },
            new() { Id = "Goblin_King", Model = $"{W}/Characters/SM_Chr_King_01.prefab", CapsuleHeight = 2.0f,
                Weapons = new[] { R($"{W}/Weapons/SM_Wep_Sword_02.prefab", PolyRightYLong), L($"{W}/Weapons/SM_Wep_Shield_02.prefab", PolyLeftShield, 0.9f) } },
        };

        /// <summary>The prefab every creator-made Viking NPC uses: the master rig (all bodies, hair and furs) plus a <see cref="ModelAppearanceBinder"/> that dresses it from the NPC's appearance entries.</summary>
        internal static readonly Entry NpcBase =
            new() { Id = "VikingNpcBase", Hero = true, Model = $"{V}/Characters/SM_Chr_Warrior_Male_01.prefab", Weapons = System.Array.Empty<Weapon>(), AppearanceBase = true };

        public const string NpcBasePath = VillagerFolder + "/VikingNpcBase.prefab";

        /// <summary>The goblin counterpart: the skeleton-only goblin master behind the same wrapper, goblin animation profile and the Sidekick yaw offsets measured on the roster goblins. Kept OUT of the combat roster folder
        /// (<c>RosterPrefabTests</c> asserts every prefab there is armed).</summary>
        internal static readonly Entry GoblinNpcBase =
            new() { Id = "GoblinNpcBase", Hero = false, Model = GoblinCatalogAuthor.MasterPath, Weapons = System.Array.Empty<Weapon>(), AppearanceBase = true, Catalog = GoblinCatalogAuthor.LoadOrBuild, Yaw = (18f, 34f), BakeDefaultLook = true };

        /// <summary>(idle, moving) hip-line yaw of a Sidekick human under the UAL idle and jog, in degrees (see <c>GameFacingTests</c>); set from the measurement in <c>VillageHumanTests.AHumansBodyFacesItsDirectionOfTravel...</c>.</summary>
        internal static readonly (float idle, float moving) HumanYaw = (-3.3f, 28.5f);   // measured by VillageHumanTests (idle -3.3, walking +28.5; the editor's SampleAnimation shows ~165 for the idle, an editor artefact the preview undoes itself)

        // NOTE: declared BEFORE HumanNpcBase on purpose: static readonly initializers run in textual order, so a later HumanYaw would be read as (0, 0).
        /// <summary>The Sidekick human: the skeleton-only human master behind the same wrapper, the HERO animation profile (UAL), and the yaw offsets MEASURED in the running game (a hero entry would otherwise silently get (0, 30)).</summary>
        internal static readonly Entry HumanNpcBase =
            new() { Id = "HumanNpcBase", Hero = true, Model = HumanCatalogAuthor.MasterPath, Weapons = System.Array.Empty<Weapon>(), AppearanceBase = true, Catalog = HumanCatalogAuthor.LoadOrBuild, Yaw = HumanYaw, BakeDefaultLook = true };

        public const string NpcFolder = Root + "/Npcs";
        public const string HumanNpcBasePath = NpcFolder + "/HumanNpcBase.prefab";
        public const string GoblinNpcBasePath = NpcFolder + "/GoblinNpcBase.prefab";

        [MenuItem("Viking Raid/Appearance/Author Human Base Prefab")]
        public static void RunHumanBase()
        {
            var profile = AssetDatabase.LoadAssetAtPath<CharacterAnimationProfile>(HeroProfilePath);
            if (profile == null) { Debug.LogError("[CharacterRosterAuthor] the hero animation profile is missing: run 'Viking Raid/Task 1/Author Characters' first."); return; }
            HumanCatalogAuthor.Build();   // the catalog and the skeleton-only master the wrapper is built around
            EnsureFolder(NpcFolder);
            BuildPrefab(HumanNpcBase, profile, NpcFolder);
            AssetDatabase.SaveAssets();
            Debug.Log($"[CharacterRosterAuthor] authored the human NPC base prefab: {HumanNpcBasePath}");
        }

        [MenuItem("Viking Raid/Appearance/Author Goblin Base Prefab")]
        public static void RunGoblinBase()
        {
            var profile = AssetDatabase.LoadAssetAtPath<CharacterAnimationProfile>(GoblinProfilePath);
            if (profile == null) { Debug.LogError("[CharacterRosterAuthor] the goblin animation profile is missing: run 'Viking Raid/Task 1/Author Characters' first."); return; }
            GoblinCatalogAuthor.Build();   // the catalog and the skeleton-only master the wrapper is built around
            EnsureFolder(NpcFolder);
            BuildPrefab(GoblinNpcBase, profile, NpcFolder);
            AssetDatabase.SaveAssets();
            Debug.Log($"[CharacterRosterAuthor] authored the goblin NPC base prefab: {GoblinNpcBasePath}");
        }

        [MenuItem("Viking Raid/Village/Author Villager Characters")]
        public static void RunVillagers()
        {
            var profile = AssetDatabase.LoadAssetAtPath<CharacterAnimationProfile>(HeroProfilePath);
            if (profile == null) { Debug.LogError("[CharacterRosterAuthor] the hero animation profile is missing: run 'Viking Raid/Task 1/Author Characters' first."); return; }
            EnsureFolder(VillagerFolder);
            BuildPrefab(NpcBase, profile, VillagerFolder);
            AssetDatabase.SaveAssets();
            Debug.Log($"[CharacterRosterAuthor] authored the Viking NPC base prefab under {VillagerFolder}");
        }

        [MenuItem("Viking Raid/Task 1/Author Characters")]
        public static void Run()
        {
            AnimationSpikeAuthor.EnsureUalHumanoid();
            BowAnimationImport.EnsureImported();
            EnsureFolder(PrefabFolder);
            EnsureFolder(AnimationFolder);

            var drawClip = BowAnimationImport.Clip("Armature|Bow_Draw");
            var bowIdle = BowAnimationImport.Clip("Armature|Bow_Idle");
            var hero = BuildProfile(HeroProfilePath, Idle: UalClip("Idle_Loop"), Moving: UalClip("Jog_Fwd_Loop"), movingSpeed: HeroMoveReferenceSpeed);
            var goblin = BuildProfile(GoblinProfilePath, Idle: PolyClip("Idles/A_POLY_GBL_Idle_Standing_Neut"), Moving: PolyClip("Locomotion/Run/A_POLY_GBL_Run_F_Neut"), movingSpeed: GoblinMoveReferenceSpeed);
            // archers: the same locomotion, but the shoot cue is the full Bow_Draw; the hero archer also idles in the bow-ready stance (goblin archers keep
            // their hunch and straighten up for the shot)
            var heroBow = BuildProfile(HeroBowProfilePath, Idle: bowIdle, Moving: UalClip("Jog_Fwd_Loop"), movingSpeed: HeroMoveReferenceSpeed, shoot: drawClip, shootCrossfade: 0.2f);
            var goblinBow = BuildProfile(GoblinBowProfilePath, Idle: PolyClip("Idles/A_POLY_GBL_Idle_Standing_Neut"), Moving: PolyClip("Locomotion/Run/A_POLY_GBL_Run_F_Neut"), movingSpeed: GoblinMoveReferenceSpeed, shoot: drawClip, shootCrossfade: 0.25f);
            if (hero == null || goblin == null || heroBow == null || goblinBow == null) return;

            foreach (var entry in Roster) BuildPrefab(entry, entry.Hero ? (entry.Bow ? heroBow : hero) : (entry.Bow ? goblinBow : goblin));
            AssetDatabase.SaveAssets();
            Debug.Log($"[CharacterRosterAuthor] authored {Roster.Length} prefabs under {PrefabFolder}");
        }

        public static IEnumerable<string> PrefabIds => Roster.Select(e => e.Id);

        // ------------------------------------------------------------------ animation

        private static AnimationClip UalClip(string name) =>
            AssetDatabase.LoadAllAssetsAtPath(Ual).OfType<AnimationClip>().FirstOrDefault(c => c.name == "Armature|" + name);

        private static AnimationClip PolyClip(string relative) =>
            AssetDatabase.LoadAllAssetsAtPath($"{GoblinLoco}/{relative}.fbx").OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));

        private static CharacterAnimationProfile BuildProfile(string path, AnimationClip Idle, AnimationClip Moving, float movingSpeed, AnimationClip shoot = null, float shootCrossfade = 0.1f)
        {
            var sources = new (string CueId, AnimationClip Clip, float Crossfade, bool Loop, float Ref)[]
            {
                ("anim-idle", Idle, 0.2f, true, 0f),
                ("anim-moving", Moving, 0.2f, true, movingSpeed),
                ("anim-attack-melee", UalClip("Sword_Attack"), 0.1f, false, 0f),
                ("anim-shoot", shoot != null ? shoot : UalClip("Spell_Simple_Shoot"), shoot != null ? shootCrossfade : 0.1f, false, 0f),
                ("anim-hit", UalClip("Hit_Chest"), 0.05f, false, 0f),
                ("anim-death", UalClip("Death01"), 0.1f, false, 0f),
            };

            var cues = new List<AnimationCueClip>();
            foreach (var (cueId, clip, crossfade, loop, reference) in sources)
            {
                if (clip == null) { Debug.LogError($"[CharacterRosterAuthor] no clip for {cueId} ({path})"); return null; }
                object boxed = new AnimationCueClip();
                SetField(boxed, "cueId", new DefinitionId(cueId));
                SetField(boxed, "clip", clip);
                SetField(boxed, "crossfadeDuration", crossfade);
                SetField(boxed, "loop", loop);
                SetField(boxed, "referenceSpeed", reference);
                cues.Add((AnimationCueClip)boxed);
            }

            var profile = AssetDatabase.LoadAssetAtPath<CharacterAnimationProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<CharacterAnimationProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }

            SetField(profile, "cues", cues);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<CharacterAnimationProfile>(path);
        }

        // ------------------------------------------------------------------ prefabs

        private static void BuildPrefab(Entry entry, CharacterAnimationProfile profile, string folder = PrefabFolder)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(entry.Model);
            if (model == null) { Debug.LogError($"[CharacterRosterAuthor] missing model {entry.Model}"); return; }

            var spec = new CharacterSetupSpec
            {
                ModelPrefab = model,
                AnimationProfile = profile,
                CapsuleHeight = entry.CapsuleHeight,
                CapsuleCenterY = entry.CapsuleHeight * 0.5f,
                CapsuleRadius = entry.CapsuleRadius,
                AgentHeight = entry.CapsuleHeight,
                AgentRadius = entry.AgentRadius,
                AgentSpeed = entry.Hero ? 4.0f : 3.0f,
                AgentAcceleration = 20f,
            };

            var path = $"{folder}/{entry.Id}.prefab";
            CharacterWrapperBuilder.BuildOrUpdate(spec, path);

            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var animator = contents.GetComponentsInChildren<Animator>(true).First(a => a.avatar != null);
                foreach (var a in contents.GetComponentsInChildren<Animator>(true))
                {
                    // Synty prefabs ship applyRootMotion=1 / CullUpdateTransforms: root motion would drift the Model off CharacterRoot (the
                    // NavMeshAgent owns movement) and culling would stop animating off-screen models.
                    a.applyRootMotion = false;
                    a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }

                foreach (var old in contents.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("Weapon_")).ToList())
                    if (old != null) Object.DestroyImmediate(old.gameObject);

                foreach (var weapon in entry.Weapons) Attach(animator, weapon, entry.Id);

                var cues = contents.GetComponent<ModelActionCues>();
                if (cues == null) cues = contents.AddComponent<ModelActionCues>();
                cues.HitPointResourceId = new DefinitionId(HitPointResourceId);

                // The toolkit raycasts only its injected hittableMask for click-to-move and ability targeting: a character left on Default is
                // invisible to both (found in the Task 3 review), so the whole hierarchy -- weapons included -- goes on the Character layer.
                WorldLayers.SetRecursive(contents, WorldLayers.Character);

                // the borrowed idle and jog clips carry a yaw when retargeted (measured in the running game, see ModelYawCorrection); each run cycle also gets its own phase
                var (idleYaw, movingYaw) = MeasuredYawOffsets(entry);
                var correction = contents.GetComponent<ModelYawCorrection>() ?? contents.AddComponent<ModelYawCorrection>();
                correction.IdleYaw = idleYaw; correction.MovingYaw = movingYaw;
                if (contents.GetComponent<AnimationPhaseOffset>() == null) contents.AddComponent<AnimationPhaseOffset>();

                if (entry.AppearanceBase)
                {
                    var binder = contents.GetComponent<ModelAppearanceBinder>() ?? contents.AddComponent<ModelAppearanceBinder>();
                    binder.Catalog = (entry.Catalog ?? ModelAppearanceCatalogAuthor.LoadOrBuild)();
                    if (entry.BakeDefaultLook) ModelAppearanceApplier.Apply(contents, binder.Catalog, new Dictionary<DefinitionId, DefinitionId>(), immediate: true);
                }

                PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static SolvedGrips SolvedGripsCache => solvedGrips ??= GripSolver.Load();
        private static SolvedGrips solvedGrips;

        /// <summary>Hip-line yaw (degrees, relative to the root) of the idle and the run/jog loops, measured by GameYawTests on the running game. Heroes use the UAL jog; the War Camp goblins the
        /// Polygon goblin clips; the Sidekick goblins the same clips on a slightly different pelvis.</summary>
        private static (float idle, float moving) MeasuredYawOffsets(Entry entry)
        {
            if (entry.Yaw.HasValue) return entry.Yaw.Value;
            if (entry.Hero) return (entry.Id == "Hero_Sigrid" ? 6f : 0f, 30f);                         // Sigrid idles in the Blender Bow_Idle
            return entry.Model.Contains("SidekickCharacters") ? (18f, 34f) : (20f, 31f);
        }

        private static void Attach(Animator animator, Weapon weapon, string characterId)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(weapon.Prefab);
            if (source == null) { Debug.LogError($"[CharacterRosterAuthor] missing weapon {weapon.Prefab}"); return; }

            var hand = animator.GetBoneTransform(weapon.Hand == Hand.Right ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            if (!string.IsNullOrEmpty(weapon.Socket))
                hand = hand.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == weapon.Socket) ?? hand;

            // A plain clone (not a nested prefab instance), so its colliders and Animator can be removed: a Rigid weapon must not add
            // physics or a second Animator to the character.
            var clone = Object.Instantiate(source);
            clone.name = weapon.Hand == Hand.Right ? "Weapon_R" : "Weapon_L";
            foreach (var c in clone.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            foreach (var a in clone.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(a);
            clone.transform.SetParent(hand, false);
            // melee weapons and shields use the grip SOLVED from the animation (GripSolver); the hand-set constants remain for bows and as the fallback
            var solved = SolvedGripsCache.grips.FirstOrDefault(g => g.key == GripSolver.Key(characterId, weapon.Prefab));
            clone.transform.localPosition = solved != null ? solved.offset : weapon.Offset;
            clone.transform.localRotation = solved != null ? Quaternion.Euler(solved.euler) : Quaternion.AngleAxis(weapon.Twist, Vector3.right) * Quaternion.Euler(weapon.Euler);
            clone.transform.localScale = Vector3.one * weapon.Scale;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null) { field.SetValue(target, value); return; }
                type = type.BaseType;
            }

            throw new System.Exception($"Field '{fieldName}' not found on {target.GetType().Name}.");
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath)) return;
            var parts = assetPath.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
