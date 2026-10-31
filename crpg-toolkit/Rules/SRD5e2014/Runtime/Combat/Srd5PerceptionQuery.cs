using System;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Rules.SRD5
{
    /// <summary>
    /// Who sees whom under the 2014 SRD. A target is unseen when the observer is blinded, the target invisible, something blocks the sight line, or (when the game supplies a narrower <see cref="VisionProfile"/>) the target is outside the observer's cone and awareness circle.
    /// Otherwise the light at the target decides: bright is clear, dim is lightly obscured, darkness is heavily obscured, which the SRD treats as the observer being blind to it. Darkvision (a feature modifier under <see cref="DarkvisionKey"/>, in feet like Speed)
    /// reads darkness as dim and dim as bright within its range. Every dependency past the first four is optional: with no light provider everything is brightly lit, with no profile provider every observer sees as the SRD does (all round, no range limit).
    /// </summary>
    public sealed class Srd5PerceptionQuery : IPerceptionQuery
    {
        public const string DarkvisionKey = "Sense:Darkvision";
        private static readonly DefinitionId Blinded = new("condition-blinded");
        private static readonly DefinitionId Invisible = new("condition-invisible");

        private readonly ICharacterRegistry characters;
        private readonly IRulesDatabase rules;
        private readonly IPositionProvider positions;
        private readonly ILineOfSightProvider lineOfSight;
        private readonly IFacingProvider facing;
        private readonly ILightProvider light;
        private readonly Func<RuntimeEntityId, VisionProfile> profiles;
        private readonly float distanceScale;

        public Srd5PerceptionQuery(ICharacterRegistry characters, IRulesDatabase rules, IPositionProvider positions, ILineOfSightProvider lineOfSight,
            IFacingProvider facing = null, ILightProvider light = null, Func<RuntimeEntityId, VisionProfile> profiles = null, float distanceScale = 1f)
        {
            this.characters = characters ?? throw new ArgumentNullException(nameof(characters));
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
            this.positions = positions ?? throw new ArgumentNullException(nameof(positions));
            this.lineOfSight = lineOfSight ?? throw new ArgumentNullException(nameof(lineOfSight));
            this.facing = facing;
            this.light = light;
            this.profiles = profiles;
            this.distanceScale = distanceScale;
        }

        /// <summary>Set by the game when it has stealth: a creature hidden from an observer reads as unseen to it.</summary>
        public IHiddenState Hidden { get; set; }

        public bool CanSee(RuntimeEntityId observer, RuntimeEntityId target) => Sees(SightOf(observer, target));

        public static bool Sees(Visibility sight) => sight == Visibility.Clear || sight == Visibility.LightlyObscured;

        /// <summary>Sight with a hider counted: a creature hidden from the observer is unseen by it.</summary>
        public Visibility SightOf(RuntimeEntityId observer, RuntimeEntityId target) =>
            Hidden != null && observer != target && Hidden.IsHiddenFrom(target, observer) ? Visibility.Unseen : BaseSightOf(observer, target);

        /// <summary>Sight from geometry, conditions and light alone: what the observer would see if the target were not hiding. The stealth rules judge hiding against this.</summary>
        public Visibility BaseSightOf(RuntimeEntityId observer, RuntimeEntityId target)
        {
            if (!characters.TryGetCharacter(observer, out var viewer) || !characters.TryGetCharacter(target, out var seen)) return Visibility.Unseen;
            if (observer == target) return Visibility.Clear;
            if (seen.HasCondition(Invisible)) return Visibility.Unseen;
            return SightFrom(observer, viewer, positions.GetPosition(target));
        }

        /// <summary>How the observer would see a creature standing at a point (no target creature involved): geometry, light and the observer's own state, with nothing hiding. The vision field asks this for every cell.</summary>
        public Visibility SightOfPoint(RuntimeEntityId observer, Vector3 point) =>
            characters.TryGetCharacter(observer, out var viewer) ? SightFrom(observer, viewer, point) : Visibility.Unseen;

        private Visibility SightFrom(RuntimeEntityId observer, CharacterState viewer, Vector3 to)
        {
            if (viewer.HasCondition(Blinded)) return Visibility.Unseen;

            var from = positions.GetPosition(observer);
            if (!lineOfSight.HasLineOfSight(from, to)) return Visibility.Unseen;
            if (profiles != null && facing != null && !VisionGeometry.InSightArea(from, facing.GetFacingYaw(observer), profiles(observer), to)) return Visibility.Unseen;

            var level = light == null ? LightLevel.Bright : light.LightAt(to);
            if (level != LightLevel.Bright && Vector3.Distance(from, to) <= DarkvisionRange(viewer)) level = level == LightLevel.Dark ? LightLevel.Dim : LightLevel.Bright;
            return level switch
            {
                LightLevel.Dim => Visibility.LightlyObscured,
                LightLevel.Dark => Visibility.HeavilyObscured,
                _ => Visibility.Clear,
            };
        }

        /// <summary>The range in world units: the authored feet times the same scale the speed rule uses (the game maps 5 feet to one unit, so 60 feet is 12).</summary>
        private float DarkvisionRange(CharacterState character)
        {
            var range = 0f;
            foreach (var (source, modifier) in FeatureModifierCollector.Collect(character, rules, DarkvisionKey))
                range = Mathf.Max(range, (float)modifier.ToRuleModifier(source).Value);
            return range * distanceScale;
        }
    }
}
