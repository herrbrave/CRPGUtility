using System.Collections.Generic;
using System.Linq;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    /// <summary>Aimed abilities (docs/Plans/SpiritOfVengeance-CellFightAndLeap.md): the shapes' geometry, the service deriving targets from an aim (a Shatter that hits everyone in its radius, a Burning Hands cone from the caster, an aura, an empty area), range / line of sight / the game's
    /// aim surface, relation filtering, the explicit cap, the relocation effect (Misty Step), half damage, and the context copy that keeps ownership and the aim.</summary>
    public class AimedAbilityTests
    {
        private static readonly DefinitionId HpId = new("resource-hp");
        private static readonly DefinitionId Heroes = new("faction-heroes"), Goblins = new("faction-goblins");

        // ------------------------------------------------------------------ fixtures
        private sealed class AlwaysMinimum : IRandomSource { public int Next(int minInclusive, int maxExclusive) => minInclusive; }   // every die shows its lowest face: a 1d6+N deals exactly N

        private sealed class Candidates : ITargetCandidateSource
        {
            public List<RuntimeEntityId> All = new();
            public IEnumerable<RuntimeEntityId> CandidatesNear(Vector3 centre, float radius) => All;
        }

        private sealed class Surface : IAimSurface
        {
            public bool Refuse; public Vector3 Snap; public bool UseSnap;
            public bool TryAccept(RuntimeEntityId source, AbilityDefinition ability, Vector3 clicked, out Vector3 point, out string reason)
            { point = UseSnap ? Snap : clicked; reason = Refuse ? "A wall is in the way." : null; return !Refuse; }
        }

        private sealed class Mover : IRelocationService
        {
            public RuntimeEntityId Moved; public Vector3 To; public RelocationStyle Style; public int Calls;
            public bool TryRelocate(RuntimeEntityId entity, Vector3 destination, RelocationStyle style) { Moved = entity; To = destination; Style = style; Calls++; return true; }
        }

        private sealed class World
        {
            public CharacterRegistry Registry = new();
            public FakePositionProvider Positions = new();
            public FakeFactionProvider Factions = new();
            public FakeLineOfSightProvider Sight = new();
            public Candidates Candidates = new();
            public ResourceDefinition Hp = TestDefinitionFactory.CreateResourceDefinition("resource-hp", "Hit Points");
            public ResourceDefinition Slot = TestDefinitionFactory.CreateResourceDefinition("resource-slot", "Slot");
            public RuntimeEntityId Caster;

            public World() { Caster = Add(Vector3.zero, Heroes, 20, withSlot: true); }

            public RuntimeEntityId Add(Vector3 at, DefinitionId faction, int hp = 20, bool withSlot = false)
            {
                var c = new CharacterState(RuntimeEntityId.NewId());
                c.AddResource(new ResourceState(HpId, max: hp));
                if (withSlot) c.AddResource(new ResourceState(Slot.Id, max: 3));
                Registry.Register(c); Positions.Set(c.Id, at); Factions.Set(c.Id, faction); Candidates.All.Add(c.Id);
                return c.Id;
            }

            public AbilityResolutionService Service(Surface surface = null, Mover mover = null, bool candidates = true) =>
                new(Registry, new DiceService(new AlwaysMinimum()), positions: Positions, factionProvider: Factions, lineOfSightProvider: Sight)
                { CandidateSource = candidates ? Candidates : null, AimSurface = surface, Relocation = mover };

            public int HpOf(RuntimeEntityId id) { Registry.TryGetCharacter(id, out var c); c.TryGetResource(HpId, out var hp); return hp.Current; }
            public int SlotsOf(RuntimeEntityId id) { Registry.TryGetCharacter(id, out var c); c.TryGetResource(Slot.Id, out var s); return s.Current; }
        }

        private static TargetingDefinition Targeting(TargetMode mode, float size = 0f, float range = 18f, TargetRelation relation = TargetRelation.Any, AreaAnchor anchor = AreaAnchor.AtAim, int cap = 0, int min = 1, int max = 1)
        {
            var t = new TargetingDefinition();
            TestDefinitionFactory.SetPrivateField(t, "mode", mode); TestDefinitionFactory.SetPrivateField(t, "areaSize", size); TestDefinitionFactory.SetPrivateField(t, "range", range);
            TestDefinitionFactory.SetPrivateField(t, "relation", relation); TestDefinitionFactory.SetPrivateField(t, "areaAnchor", anchor); TestDefinitionFactory.SetPrivateField(t, "areaTargetCap", cap);
            TestDefinitionFactory.SetPrivateField(t, "minTargets", min); TestDefinitionFactory.SetPrivateField(t, "maxTargets", max);
            return t;
        }

        private static DamageEffect Damage(World w, int flat, bool halved = false)
        {
            var d = new DamageEffect();
            TestDefinitionFactory.SetPrivateField(d, "hitPointResource", w.Hp);
            TestDefinitionFactory.SetPrivateField(d, "amount", new DiceExpression(1, 6, flat - 1));
            TestDefinitionFactory.SetPrivateField(d, "halved", halved);
            return d;
        }

        private static AbilityDefinition Ability(World w, TargetingDefinition targeting, params AbilityEffect[] effects)
        {
            var a = ScriptableObject.CreateInstance<AbilityDefinition>();
            TestDefinitionFactory.SetPrivateField(a, "id", "ability-aimed-test");
            TestDefinitionFactory.SetPrivateField(a, "costs", new List<AbilityCost> { Cost(w.Slot, 1) });
            TestDefinitionFactory.SetPrivateField(a, "effects", effects.ToList());
            TestDefinitionFactory.SetPrivateField(a, "targeting", targeting);
            return a;
        }

        private static AbilityCost Cost(ResourceDefinition resource, int amount)
        {
            object cost = new AbilityCost();
            TestDefinitionFactory.SetPrivateField(cost, "resource", resource); TestDefinitionFactory.SetPrivateField(cost, "amount", amount);
            return (AbilityCost)cost;
        }

        private static AbilityAim Aim(World w, Vector3 point) => AbilityAim.FromCaster(w.Positions.GetPosition(w.Caster), point);

        // ------------------------------------------------------------------ shapes
        [TestCase(10f, 0f, 2f, true)] [TestCase(10f, 0f, 3.5f, false)] [TestCase(10f, 2.5f, 0f, true)] [TestCase(10f, 3.5f, 0f, false)]
        public void ASphereIsARadiusAroundTheAimPoint(float x, float y, float z, bool inside)
        {
            var t = Targeting(TargetMode.Sphere, 3f);
            Assert.AreEqual(inside, AreaShapes.Contains(t, Vector3.zero, new AbilityAim(new Vector3(10f, 0f, 0f), Vector3.right), new Vector3(x, y, z)));
        }

        [TestCase(3f, 0f, 1f, true)] [TestCase(3f, 0f, 2f, false)] [TestCase(5f, 0f, 0f, false)] [TestCase(-1f, 0f, 0f, false)] [TestCase(0.5f, 0f, 0.2f, true)]
        public void AConeStartsAtTheCasterAndOpensToTheLengthItself(float x, float y, float z, bool inside)
        {
            var t = Targeting(TargetMode.Cone, 4.5f, range: 0f, anchor: AreaAnchor.AtCaster);
            Assert.AreEqual(inside, AreaShapes.Contains(t, Vector3.zero, new AbilityAim(new Vector3(8f, 0f, 0f), Vector3.right), new Vector3(x, y, z)));
        }

        [TestCase(3f, 0f, 0.7f, true)] [TestCase(3f, 0f, 0.9f, false)] [TestCase(6.1f, 0f, 0f, false)] [TestCase(-0.5f, 0f, 0f, false)]
        public void ALineIsAThinStripFromTheCaster(float x, float y, float z, bool inside)
        {
            var t = Targeting(TargetMode.Line, 6f, range: 0f);
            Assert.AreEqual(inside, AreaShapes.Contains(t, Vector3.zero, new AbilityAim(new Vector3(8f, 0f, 0f), Vector3.right), new Vector3(x, y, z)));
        }

        [TestCase(12.5f, 0f, 2.5f, true)] [TestCase(13.5f, 0f, 0f, false)] [TestCase(10f, 0f, -3.1f, false)] [TestCase(10f, 2.5f, 0f, false)]
        public void ACubeIsASquareOfItsSideAtTheAimPoint(float x, float y, float z, bool inside)
        {
            var t = Targeting(TargetMode.Area, 6f);
            Assert.AreEqual(inside, AreaShapes.Contains(t, Vector3.zero, new AbilityAim(new Vector3(10f, 0f, 0f), Vector3.right), new Vector3(x, y, z)));
        }

        [TestCase(4f, 0f, 0f, true)] [TestCase(-1f, 0f, 0f, false)] [TestCase(5f, 0f, 0f, false)]
        public void ACubeOnTheCasterTouchesHimInTheAimDirection(float x, float y, float z, bool inside)
        {
            var t = Targeting(TargetMode.Area, 4.5f, range: 0f, anchor: AreaAnchor.AtCaster);
            Assert.AreEqual(inside, AreaShapes.Contains(t, Vector3.zero, new AbilityAim(new Vector3(8f, 0f, 0f), Vector3.right), new Vector3(x, y, z)));
        }

        [Test]
        public void ACylinderIsARadiusAndAHeightAndAPointHitsNobody()
        {
            var cylinder = Targeting(TargetMode.Cylinder, 3f); var aim = new AbilityAim(new Vector3(10f, 0f, 0f), Vector3.right);
            Assert.IsTrue(AreaShapes.Contains(cylinder, Vector3.zero, aim, new Vector3(10f, 4f, 2f)));
            Assert.IsFalse(AreaShapes.Contains(cylinder, Vector3.zero, aim, new Vector3(10f, 7f, 0f)));
            Assert.IsFalse(AreaShapes.Contains(Targeting(TargetMode.Point), Vector3.zero, aim, new Vector3(10f, 0f, 0f)));
        }

        [Test]
        public void TheTargetingFlagsSayWhatNeedsAnAimAndWhatIsCastAtOnce()
        {
            Assert.IsTrue(Targeting(TargetMode.Point).RequiresAim); Assert.IsTrue(Targeting(TargetMode.Cone, 4.5f, anchor: AreaAnchor.AtCaster).RequiresAim);
            Assert.IsFalse(Targeting(TargetMode.SingleUnit).RequiresAim); Assert.IsFalse(Targeting(TargetMode.Self).RequiresAim);
            var aura = Targeting(TargetMode.Sphere, 3f, anchor: AreaAnchor.AtCaster);
            Assert.IsFalse(aura.RequiresAim); Assert.IsTrue(aura.CasterCentred);
            Assert.IsTrue(Targeting(TargetMode.Point).UsesRange); Assert.IsTrue(Targeting(TargetMode.Sphere, 3f).UsesRange);
            Assert.IsFalse(Targeting(TargetMode.Cone, 4.5f, anchor: AreaAnchor.AtCaster).UsesRange); Assert.IsFalse(Targeting(TargetMode.Area, 4.5f, anchor: AreaAnchor.AtCaster).UsesRange);
        }

        // ------------------------------------------------------------------ the service
        [Test]
        public void TheCreaturePathRefusesAnAimedAbilityAndAimedRefusesACreatureOne()
        {
            var w = new World(); var foe = w.Add(new Vector3(5f, 0f, 0f), Goblins);
            var shatter = Ability(w, Targeting(TargetMode.Sphere, 3f), Damage(w, 3));
            var result = w.Service().Validate(w.Caster, shatter, new[] { foe });
            Assert.AreEqual(AbilityRejectionReason.AimRequired, result.Reason);
            var single = Ability(w, Targeting(TargetMode.SingleUnit));
            Assert.IsFalse(w.Service().Validate(w.Caster, single, Aim(w, Vector3.right)).IsValid);
        }

        [Test]
        public void AShatterHitsEveryoneInItsRadiusIncludingAnAllyAndNobodyOutsideAndSpendsTheSlotOnce()
        {
            var w = new World();
            var foe = w.Add(new Vector3(10f, 0f, 0f), Goblins); var ally = w.Add(new Vector3(10f, 0f, 2f), Heroes); var far = w.Add(new Vector3(10f, 0f, 6f), Goblins);
            var shatter = Ability(w, Targeting(TargetMode.Sphere, 3f, range: 18f), Damage(w, 4));
            var result = w.Service().Execute(w.Caster, shatter, Aim(w, new Vector3(10f, 0f, 0f)));
            Assert.IsTrue(result.Succeeded, result.Message);
            Assert.AreEqual(16, w.HpOf(foe)); Assert.AreEqual(16, w.HpOf(ally), "friendly fire: a Shatter hits whoever stands in it"); Assert.AreEqual(20, w.HpOf(far));
            Assert.AreEqual(2, w.SlotsOf(w.Caster));
        }

        [Test]
        public void AnEmptyAreaIsALegalCastAndStillCosts()
        {
            var w = new World(); var shatter = Ability(w, Targeting(TargetMode.Sphere, 3f), Damage(w, 4));
            var result = w.Service().Execute(w.Caster, shatter, Aim(w, new Vector3(10f, 0f, 0f)));
            Assert.IsTrue(result.Succeeded); Assert.AreEqual(2, w.SlotsOf(w.Caster));
        }

        [Test]
        public void AnAimBeyondTheRangeOrBehindAWallIsRefused()
        {
            var w = new World(); var shatter = Ability(w, Targeting(TargetMode.Sphere, 3f, range: 8f), Damage(w, 4));
            Assert.AreEqual(AbilityRejectionReason.OutOfRange, w.Service().Validate(w.Caster, shatter, Aim(w, new Vector3(9f, 0f, 0f))).Reason);
            w.Sight.AlwaysBlocked = true;
            Assert.AreEqual(AbilityRejectionReason.NoLineOfSight, w.Service().Validate(w.Caster, shatter, Aim(w, new Vector3(5f, 0f, 0f))).Reason);
            Assert.AreEqual(2 + 1, w.SlotsOf(w.Caster), "a refusal charges nothing");
        }

        [Test]
        public void TheGamesAimSurfaceCanRefuseOrSnapTheClickedPoint()
        {
            var w = new World(); var mistyStep = Ability(w, Targeting(TargetMode.Point, range: 9f), new RelocateEffect());
            var mover = new Mover();
            var refuse = w.Service(new Surface { Refuse = true }, mover).Validate(w.Caster, mistyStep, Aim(w, new Vector3(5f, 0f, 0f)));
            Assert.AreEqual(AbilityRejectionReason.InvalidAimPoint, refuse.Reason); StringAssert.Contains("wall", refuse.Message);
            var snap = new Vector3(5.5f, 0f, 0.5f);
            Assert.IsTrue(w.Service(new Surface { UseSnap = true, Snap = snap }, mover).Execute(w.Caster, mistyStep, Aim(w, new Vector3(5f, 0f, 0f))).Succeeded);
            Assert.AreEqual(snap, mover.To, "the effect lands on the snapped point");
        }

        [Test]
        public void MistyStepMovesTheCasterThroughTheRelocationServiceAndIsRefusedWithoutOne()
        {
            var w = new World(); var mistyStep = Ability(w, Targeting(TargetMode.Point, range: 9f), new RelocateEffect());
            var mover = new Mover();
            Assert.IsTrue(w.Service(mover: mover).Execute(w.Caster, mistyStep, Aim(w, new Vector3(6f, 0f, 0f))).Succeeded);
            Assert.AreEqual(w.Caster, mover.Moved); Assert.AreEqual(new Vector3(6f, 0f, 0f), mover.To); Assert.AreEqual(RelocationStyle.Teleport, mover.Style); Assert.AreEqual(2, w.SlotsOf(w.Caster));
            var none = w.Service();   // no relocation service wired
            var refused = none.Validate(w.Caster, mistyStep, Aim(w, new Vector3(6f, 0f, 0f)));
            Assert.AreEqual(AbilityRejectionReason.MissingRequiredEffectService, refused.Reason);
            Assert.AreEqual(2, w.SlotsOf(w.Caster), "no charge when the service is missing");
        }

        [Test]
        public void ARelationOfEnemyLeavesAlliesAndTheUnknownOutButAnyHitsThemAll()
        {
            var w = new World();
            var foe = w.Add(new Vector3(10f, 0f, 0f), Goblins); var ally = w.Add(new Vector3(10f, 0f, 1f), Heroes);
            var stranger = new CharacterState(RuntimeEntityId.NewId()); stranger.AddResource(new ResourceState(HpId, 20)); w.Registry.Register(stranger); w.Positions.Set(stranger.Id, new Vector3(10f, 0f, -1f)); w.Candidates.All.Add(stranger.Id);   // no faction
            var enemies = Ability(w, Targeting(TargetMode.Sphere, 3f, relation: TargetRelation.Enemy));
            CollectionAssert.AreEquivalent(new[] { foe }, w.Service().ResolveTargets(w.Caster, enemies, Aim(w, new Vector3(10f, 0f, 0f))));
            var any = Ability(w, Targeting(TargetMode.Sphere, 3f, relation: TargetRelation.Any));
            CollectionAssert.AreEquivalent(new[] { foe, ally, stranger.Id }, w.Service().ResolveTargets(w.Caster, any, Aim(w, new Vector3(10f, 0f, 0f))));
        }

        [Test]
        public void ABurningHandsConeFromTheCasterHitsOnlyWhatIsInItNeverTheCasterAndMinMaxTargetsDoNotApply()
        {
            var w = new World();
            var near = w.Add(new Vector3(3f, 0f, 0.5f), Goblins); var also = w.Add(new Vector3(4f, 0f, -1.5f), Goblins); var wide = w.Add(new Vector3(3f, 0f, 3f), Goblins);
            var hands = Ability(w, Targeting(TargetMode.Cone, 4.5f, range: 0f, relation: TargetRelation.Enemy, anchor: AreaAnchor.AtCaster, max: 1), Damage(w, 5));
            Assert.IsTrue(w.Service().Execute(w.Caster, hands, Aim(w, new Vector3(8f, 0f, 0f))).Succeeded);
            Assert.AreEqual(15, w.HpOf(near)); Assert.AreEqual(15, w.HpOf(also), "maxTargets 1 does not cap an area"); Assert.AreEqual(20, w.HpOf(wide)); Assert.AreEqual(20, w.HpOf(w.Caster));
        }

        [Test]
        public void TheExplicitCapKeepsTheNearestToTheOrigin()
        {
            var w = new World();
            var a = w.Add(new Vector3(10f, 0f, 0.5f), Goblins); var b = w.Add(new Vector3(10f, 0f, 1.5f), Goblins); var c = w.Add(new Vector3(10f, 0f, 2.5f), Goblins);
            var capped = Ability(w, Targeting(TargetMode.Sphere, 3f, cap: 2));
            CollectionAssert.AreEqual(new[] { a, b }, w.Service().ResolveTargets(w.Caster, capped, Aim(w, new Vector3(10f, 0f, 0f))));
        }

        [Test]
        public void AnAuraIsOnTheCasterNeedsNoAimAndSparesHim()
        {
            var w = new World(); var near = w.Add(new Vector3(2f, 0f, 0f), Goblins); var far = w.Add(new Vector3(9f, 0f, 0f), Goblins);
            var aura = Ability(w, Targeting(TargetMode.Sphere, 3f, anchor: AreaAnchor.AtCaster, relation: TargetRelation.Enemy), Damage(w, 3));
            Assert.IsTrue(w.Service().Validate(w.Caster, aura, AbilityAim.None).IsValid);
            Assert.IsTrue(w.Service().Execute(w.Caster, aura, AbilityAim.None).Succeeded);
            Assert.AreEqual(17, w.HpOf(near)); Assert.AreEqual(20, w.HpOf(far)); Assert.AreEqual(20, w.HpOf(w.Caster));
        }

        [Test]
        public void AShapeWithNoCandidateSourceIsRefusedNeverResolvedToNobody()
        {
            var w = new World(); var shatter = Ability(w, Targeting(TargetMode.Sphere, 3f));
            Assert.AreEqual(AbilityRejectionReason.MissingRequiredEffectService, w.Service(candidates: false).Validate(w.Caster, shatter, Aim(w, new Vector3(5f, 0f, 0f))).Reason);
        }

        [Test]
        public void HalvedDamageIsHalfTheRolledAmountRoundedDown()
        {
            var w = new World(); var foe = w.Add(new Vector3(5f, 0f, 0f), Goblins);
            var halved = Ability(w, Targeting(TargetMode.Sphere, 3f), Damage(w, 7, halved: true));
            Assert.IsTrue(w.Service().Execute(w.Caster, halved, Aim(w, new Vector3(5f, 0f, 0f))).Succeeded);
            Assert.AreEqual(17, w.HpOf(foe));
        }

        [Test]
        public void ANarrowedContextKeepsTheOwnershipTrackingAndTheAim()
        {
            var w = new World(); var owned = new HashSet<RuntimeEntityId>(); var owner = EffectOwnerId.NewOwner();
            var aim = new AbilityAim(new Vector3(1f, 0f, 2f), Vector3.forward);
            var context = new AbilityExecutionContext(w.Caster, new[] { w.Caster }, w.Registry, new DiceService(new AlwaysMinimum()), null, ownerId: owner, ownedTargets: owned, aim: aim);
            var narrowed = context.Narrow(new RuntimeEntityId[0]);
            Assert.AreEqual(owner, narrowed.OwnerId); Assert.AreSame(owned, narrowed.OwnedTargets); Assert.IsTrue(narrowed.HasAim); Assert.AreEqual(aim.Point, narrowed.Aim.Point);
        }
    }
}
