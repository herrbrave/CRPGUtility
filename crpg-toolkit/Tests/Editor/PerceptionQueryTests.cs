using System;
using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Rules;
using CRPG.Rules.SRD5;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>The perception layer (who can see whom): pure cone geometry in Core and the SRD5 query on top (blindness, invisibility, obstruction, light, darkvision, cones), plus the SRD rule that an unseen attacker has advantage and an attacker who cannot see its target has disadvantage.</summary>
    public sealed class PerceptionQueryTests
    {
        private sealed class FakeLight : ILightProvider
        {
            public Func<Vector3, LightLevel> Level = _ => LightLevel.Bright;
            public LightLevel LightAt(Vector3 point) => Level(point);
        }

        private sealed class FakeFacing : IFacingProvider
        {
            public readonly Dictionary<RuntimeEntityId, float> Yaw = new();
            public float GetFacingYaw(RuntimeEntityId entity) => Yaw.TryGetValue(entity, out var yaw) ? yaw : 0f;
        }

        private RulesDatabase rules;
        private CharacterRegistry characters;
        private CharacterState viewer, subject;
        private FakePositionProvider positions;
        private FakeLineOfSightProvider sight;
        private FakeLight light;
        private FakeFacing facing;
        private Srd5PerceptionQuery query;
        private readonly List<UnityEngine.Object> owned = new();
        private static DefinitionId Id(string value) => new(value);

        [SetUp]
        public void SetUp()
        {
            rules = new RulesDatabase();
            var provider = AssetDatabase.LoadAssetAtPath<SRD5RulesetProvider>("Packages/com.crpgtoolkit.core/Rules/SRD5e2014/Content/SRD5RulesetProvider.asset");
            Assert.That(provider, Is.Not.Null);
            rules.RegisterRange(provider.Content);
            var darkvision = TestDefinitionFactory.CreateFeatureDefinition("feature-test-darkvision", "Darkvision", TestDefinitionFactory.CreateFeatureModifier(Srd5PerceptionQuery.DarkvisionKey, RuleModifierOperation.Add, 60f));
            owned.Add(darkvision); rules.Register(darkvision);
            characters = new CharacterRegistry(rules);
            viewer = new CharacterState(RuntimeEntityId.NewId()); subject = new CharacterState(RuntimeEntityId.NewId());
            characters.Register(viewer); characters.Register(subject);
            positions = new FakePositionProvider(); positions.Set(viewer.Id, Vector3.zero); positions.Set(subject.Id, new Vector3(0f, 0f, 10f));
            sight = new FakeLineOfSightProvider(); light = new FakeLight(); facing = new FakeFacing();
            query = new Srd5PerceptionQuery(characters, rules, positions, sight, facing, light, distanceScale: 0.2f);
        }

        [TearDown]
        public void TearDown() { foreach (var value in owned) UnityEngine.Object.DestroyImmediate(value); owned.Clear(); }

        private void Give(CharacterState who, string condition) => who.ApplyCondition(new ConditionInstance(Id(condition), who.Id, who.Id));

        // geometry

        [Test]
        public void TheConeSeesAheadWithinItsHalfAngleAndNotBehindOrBeyondItsRange()
        {
            var profile = new VisionProfile(coneHalfAngle: 60f, coneRange: 20f, awarenessRadius: 2f);
            var eye = Vector3.zero;
            Assert.That(VisionGeometry.InSightArea(eye, 0f, profile, new Vector3(0f, 0f, 10f)), Is.True, "straight ahead (yaw 0 faces +Z)");
            Assert.That(VisionGeometry.InSightArea(eye, 0f, profile, new Vector3(8.6f, 0f, 5f)), Is.True, "about 59 degrees off the axis");
            Assert.That(VisionGeometry.InSightArea(eye, 0f, profile, new Vector3(10f, 0f, 5f)), Is.False, "about 63 degrees off the axis");
            Assert.That(VisionGeometry.InSightArea(eye, 0f, profile, new Vector3(0f, 0f, -10f)), Is.False, "behind");
            Assert.That(VisionGeometry.InSightArea(eye, 0f, profile, new Vector3(0f, 0f, 25f)), Is.False, "ahead but past the cone's range");
            Assert.That(VisionGeometry.InSightArea(eye, 90f, profile, new Vector3(10f, 0f, 0f)), Is.True, "yaw 90 faces +X");
        }

        [Test]
        public void TheAwarenessCircleSeesAllRoundAtCloseRangeAndPanoramicSeesEverythingAtAnyRange()
        {
            var profile = new VisionProfile(60f, 20f, 2f);
            Assert.That(VisionGeometry.InSightArea(Vector3.zero, 0f, profile, new Vector3(0f, 0f, -1.5f)), Is.True, "right behind, inside the awareness circle");
            Assert.That(VisionGeometry.InSightArea(Vector3.zero, 0f, profile, new Vector3(0f, 0f, -2.5f)), Is.False);
            Assert.That(VisionGeometry.InSightArea(Vector3.zero, 0f, VisionProfile.Panoramic, new Vector3(-400f, 0f, -900f)), Is.True, "no cone and no range limit: the SRD's own sight");
        }

        // the SRD5 query

        [Test]
        public void AnOpenBrightlyLitTargetIsSeenClearly()
        {
            Assert.That(query.SightOf(viewer.Id, subject.Id), Is.EqualTo(Visibility.Clear));
            Assert.That(query.CanSee(viewer.Id, subject.Id), Is.True);
        }

        [Test]
        public void ObstructionABlindObserverAndAnInvisibleTargetEachMakeTheTargetUnseen()
        {
            sight.AlwaysBlocked = true;
            Assert.That(query.SightOf(viewer.Id, subject.Id), Is.EqualTo(Visibility.Unseen), "something is in the way");
            sight.AlwaysBlocked = false;
            Give(viewer, "condition-blinded");
            Assert.That(query.SightOf(viewer.Id, subject.Id), Is.EqualTo(Visibility.Unseen), "blind");
            viewer.RemoveConditions(c => true); Give(subject, "condition-invisible");
            Assert.That(query.SightOf(viewer.Id, subject.Id), Is.EqualTo(Visibility.Unseen), "invisible");
            Assert.That(query.CanSee(viewer.Id, subject.Id), Is.False);
        }

        [Test]
        public void DimLightIsLightlyObscuredAndDarknessIsHeavilyObscuredAtTheTarget()
        {
            light.Level = _ => LightLevel.Dim;
            Assert.That(query.SightOf(viewer.Id, subject.Id), Is.EqualTo(Visibility.LightlyObscured));
            Assert.That(query.CanSee(viewer.Id, subject.Id), Is.True, "lightly obscured is still seen (with disadvantage on Perception, not for attacks)");
            light.Level = _ => LightLevel.Dark;
            Assert.That(query.SightOf(viewer.Id, subject.Id), Is.EqualTo(Visibility.HeavilyObscured));
            Assert.That(query.CanSee(viewer.Id, subject.Id), Is.False, "heavily obscured counts as unseen: the observer is effectively blind to it");
        }

        [Test]
        public void DarkvisionLiftsLightOneStepWithinItsRangeAndNotBeyondIt()
        {
            viewer.GrantFeature(Id("feature-test-darkvision"));
            light.Level = _ => LightLevel.Dark;
            Assert.That(query.SightOf(viewer.Id, subject.Id), Is.EqualTo(Visibility.LightlyObscured), "darkness reads as dim light within 60 feet (12 units)");
            light.Level = _ => LightLevel.Dim;
            Assert.That(query.SightOf(viewer.Id, subject.Id), Is.EqualTo(Visibility.Clear), "dim reads as bright");
            positions.Set(subject.Id, new Vector3(0f, 0f, 25f));
            Assert.That(query.SightOf(viewer.Id, subject.Id), Is.EqualTo(Visibility.LightlyObscured), "beyond the range nothing changes");
            light.Level = _ => LightLevel.Dark;
            Assert.That(query.SightOf(viewer.Id, subject.Id), Is.EqualTo(Visibility.HeavilyObscured));
        }

        [Test]
        public void AConeFromTheProfileProviderHidesWhatIsBehindTheObserverUntilItTurnsToFaceIt()
        {
            query = new Srd5PerceptionQuery(characters, rules, positions, sight, facing, light, profiles: _ => new VisionProfile(60f, 20f, 2f));
            facing.Yaw[viewer.Id] = 180f;
            Assert.That(query.SightOf(viewer.Id, subject.Id), Is.EqualTo(Visibility.Unseen), "the subject is 10 m ahead (+Z) but the observer faces -Z");
            facing.Yaw[viewer.Id] = 0f;
            Assert.That(query.SightOf(viewer.Id, subject.Id), Is.EqualTo(Visibility.Clear));
        }

        // the unseen-attacker rule

        [Test]
        public void AnAttackerTheTargetCannotSeeHasAdvantageAndOneWhoCannotSeeItsTargetHasDisadvantage()
        {
            var attacks = new DefaultAttackBonusRule(characters, rules) { Perception = query };
            Assert.That(attacks.ComputeAdvantage(viewer.Id, subject.Id), Is.EqualTo(AdvantageState.Normal));
            light.Level = p => p.z < 5f ? LightLevel.Dark : LightLevel.Bright;   // the attacker stands in the dark, the target in the light
            Assert.That(attacks.ComputeAdvantage(viewer.Id, subject.Id), Is.EqualTo(AdvantageState.Advantage), "the target cannot see the attacker");
            Assert.That(attacks.ComputeAdvantage(subject.Id, viewer.Id), Is.EqualTo(AdvantageState.Disadvantage), "and the other way round the attacker cannot see its target");
            light.Level = _ => LightLevel.Dark;
            Assert.That(attacks.ComputeAdvantage(viewer.Id, subject.Id), Is.EqualTo(AdvantageState.Normal), "neither sees the other: they cancel");
        }

        [Test]
        public void AnInvisibleAttackerHasAdvantageAndAttacksAgainstAnInvisibleTargetHaveDisadvantage()
        {
            var attacks = new DefaultAttackBonusRule(characters, rules) { Perception = query };
            Give(viewer, "condition-invisible");
            Assert.That(attacks.ComputeAdvantage(viewer.Id, subject.Id), Is.EqualTo(AdvantageState.Advantage));
            Assert.That(attacks.ComputeAdvantage(subject.Id, viewer.Id), Is.EqualTo(AdvantageState.Disadvantage));
        }

        [Test]
        public void WithoutAPerceptionQueryAttackAdvantageIsExactlyWhatItWas()
        {
            var attacks = new DefaultAttackBonusRule(characters, rules);
            light.Level = _ => LightLevel.Dark; sight.AlwaysBlocked = true;
            Assert.That(attacks.ComputeAdvantage(viewer.Id, subject.Id), Is.EqualTo(AdvantageState.Normal), "no query, no sight rule");
            Assert.That(attacks.ComputeAdvantage(subject.Id, viewer.Id), Is.EqualTo(AdvantageState.Normal));
        }
    }
}
