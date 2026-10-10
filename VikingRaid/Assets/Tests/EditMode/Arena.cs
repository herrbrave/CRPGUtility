using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Abilities;
using CRPG.AI;
using CRPG.Actors;
using CRPG.Combat;
using CRPG.Core;
using CRPG.Rules.SRD5;
using UnityEngine;
using VikingRaid.Game;

namespace VikingRaid.Tests.EditMode
{
    public sealed class SimPositions : IPositionProvider
    {
        private readonly Dictionary<RuntimeEntityId, Vector3> positions = new();
        public void Set(RuntimeEntityId id, Vector3 position) => positions[id] = position;
        public Vector3 GetPosition(RuntimeEntityId entity) => positions.TryGetValue(entity, out var p) ? p : Vector3.zero;
    }

    /// <summary>Moves a combatant straight toward a destination, for real (positions change), clamped by the budget it is given.</summary>
    public sealed class SimMover : IMovementController
    {
        private readonly SimPositions positions;
        public SimMover(SimPositions positions) => this.positions = positions;

        public float TryMoveTowards(RuntimeEntityId entity, Vector3 destination, float maxDistance)
        {
            var from = positions.GetPosition(entity);
            var distance = Vector3.Distance(from, destination);
            if (distance <= 1e-4f || maxDistance <= 0f) return 0f;
            var moved = Mathf.Min(distance, maxDistance);
            positions.Set(entity, Vector3.MoveTowards(from, destination, moved));
            return moved;
        }
    }

    public sealed class Fighter
    {
        public RuntimeEntityId Id;
        public UnitSpec Spec;
        public CharacterState State;
        public ITurnDecisionProvider Brain; // goblins only: the real brain behind the same zero-score veto the game uses
        public bool Hero => Spec.Hero;
    }

    public struct FightResult
    {
        public bool Won, Stalemate;
        public int Rounds, HeroesDowned, FarmedDownedHero;
        public float HeroHpLost;      // fraction of the party's total max HP; a hero who was ever downed counts as full HP lost
        public Dictionary<string, int> DamageTakenByHero;
    }

    /// <summary>
    /// A pure-C# fight with the REAL services (no GameObjects, no NavMesh): rolled initiative and turn-resource refills through CombatEncounterService, real
    /// attack/AC rules, EncounterFactionProvider (so Ally/Enemy relations are enforced), death through DeathSaveService (heroes go down and roll death
    /// saves, goblins die) and a CreatureDied handler removing the dead from the encounter, ConditionDurationService on the same bus (so Reckless expires), a
    /// fake position provider and mover. Goblins decide with their authored behaviour sets and replicate TacticalAiTurnDriver's loop EXACTLY (keep asking until
    /// null, at most 8 decisions, give up after 3 rejected starts -- after attacking, a goblin still "moves toward" its stop distance, which does nothing when
    /// already inside it). Heroes use a scripted policy (the player drives them in the game): bonus actions BEFORE the Action, then move / attack. Used by the
    /// rules tests, the AI tests and the balance simulation.
    /// </summary>
    public sealed class Arena : IDisposable
    {
        public readonly ContentFixture Content;
        public readonly int Seed;
        public readonly GameEventBus Bus = new();
        public readonly CharacterRegistry Chars = new();
        public readonly SimPositions Positions = new();
        public readonly CombatLog Log = new();
        public readonly DiceService Dice;
        public readonly CombatEncounterService Encounter;
        public readonly AbilityResolutionService Abilities;
        public readonly MovementResolutionService Movement;
        public readonly ConditionDurationService Durations;
        public readonly DeathSaveService DeathSaves;
        public readonly List<Fighter> Fighters = new();
        public readonly List<string> Diagnostics = new();

        private readonly Dictionary<RuntimeEntityId, Fighter> byId = new();
        private readonly HashSet<RuntimeEntityId> heroesEverDowned = new();
        private readonly DefinitionId hp = new(GameDesign.Hp), bonus = new(GameDesign.BonusAction), movement = new(GameDesign.Movement), unconscious = new("condition-unconscious");
        private readonly Dictionary<string, int> damageByHero = new();
        private int farmedDownedHero;

        public Arena(ContentFixture content, int seed)
        {
            Content = content;
            Seed = seed;
            Dice = new DiceService(new SeededRandomSource(seed));

            var db = content.Db;
            var turnResources = new TurnResourceSet(new[]
            {
                content.Resolve<CRPG.Actors.ResourceDefinition>(GameDesign.Action), content.Resolve<CRPG.Actors.ResourceDefinition>(GameDesign.BonusAction),
                content.Resolve<CRPG.Actors.ResourceDefinition>(GameDesign.Reaction), content.Resolve<CRPG.Actors.ResourceDefinition>(GameDesign.Movement),
            });
            Encounter = new CombatEncounterService(Chars, new DefaultInitiativeRule(Chars, db, Dice), turnResources, Bus, Log);
            Abilities = new AbilityResolutionService(Chars, Dice, Log, Bus, Positions, null, content.AttackRule(Chars), content.ArmorRule(Chars), null, null, db,
                new EncounterFactionProvider(Encounter), null);
            Movement = new MovementResolutionService(Chars, Positions, new SimMover(Positions), Log);
            Durations = new ConditionDurationService(Encounter, Chars, Bus, Log); // same bus the encounter publishes on, or Reckless never expires
            DeathSaves = new DeathSaveService(Chars, Dice, Bus, hp, unconscious, Log, usesDeathSaves: IsHero, canDie: id => !IsHero(id), reviveOnHeal: true);   // the real game's configuration

            Bus.Subscribe(GameEvent.CreatureDied, ctx => Encounter.Encounter?.RemoveCombatant(ctx.Target));
            Bus.Subscribe(GameEvent.CreatureDroppedToZeroHP, ctx => { if (IsHero(ctx.Target)) heroesEverDowned.Add(ctx.Target); });
            Log.EntryLogged += entry =>
            {
                if (entry is DamageLogEntry damage && byId.TryGetValue(damage.Target, out var victim) && victim.Hero)
                    damageByHero[victim.Spec.Id] = damageByHero.GetValueOrDefault(victim.Spec.Id) + damage.Amount;
            };
        }

        public void Dispose() => Durations.Dispose();

        public bool IsHero(RuntimeEntityId id) => byId.TryGetValue(id, out var f) && f.Hero;
        public Fighter Get(RuntimeEntityId id) => byId[id];
        public Fighter Find(string unitId) => Fighters.First(f => f.Spec.Id == unitId);

        public Fighter Add(string unitId, Vector3 position)
        {
            var spec = GameDesign.Unit(unitId);
            var state = Content.Spawn(Chars, unitId);
            var fighter = new Fighter { Id = state.Id, Spec = spec, State = state };
            if (!spec.Hero)
            {
                var set = Content.Resolve<BehaviorSetDefinition>(spec.Brain);
                fighter.Brain = new UtilityAIDecisionProvider(set, Content.Db, Chars, Abilities, Positions, new SeededRandomSource(Seed * 7919 + Fighters.Count), scoreFloor: 0f);
            }

            Fighters.Add(fighter);
            byId[state.Id] = fighter;
            Positions.Set(state.Id, position);
            return fighter;
        }

        public void Start() => Encounter.StartCombat(Fighters.Select(f => (f.Id, new DefinitionId(f.Hero ? GameDesign.HeroesFaction : GameDesign.GoblinsFaction))));

        // ------------------------------------------------------------------ small queries
        public int Hp(Fighter f) => f.State.TryGetResource(hp, out var r) ? r.Current : 0;
        public int MaxHp(Fighter f) => f.State.TryGetResource(hp, out var r) ? r.Max : 0;
        public float HpFraction(Fighter f) => MaxHp(f) == 0 ? 0f : (float)Hp(f) / MaxHp(f);
        public float Distance(Fighter a, Fighter b) => Vector3.Distance(Positions.GetPosition(a.Id), Positions.GetPosition(b.Id));
        private bool Has(Fighter f, string resourceId) => f.State.TryGetResource(new DefinitionId(resourceId), out var r) && r.Current > 0;

        public IEnumerable<Fighter> LivingEnemiesOf(Fighter f) =>
            (Encounter.Encounter?.GetEnemiesOf(f.Id) ?? new List<RuntimeEntityId>()).Select(Get).Where(e => Hp(e) > 0);

        private bool GoblinsWiped() => !Fighters.Any(f => !f.Hero && Hp(f) > 0 && Encounter.Encounter.Combatants.Any(c => c.Id == f.Id));
        private bool HeroesAllDown() => Fighters.Where(f => f.Hero).All(f => Hp(f) <= 0);

        // ------------------------------------------------------------------ abilities
        public bool Cast(Fighter caster, string abilityId, params Fighter[] targets)
        {
            var result = Abilities.Execute(caster.Id, Content.Resolve<AbilityDefinition>(abilityId), targets.Select(t => t.Id).ToList());
            return result.Succeeded;
        }

        // ------------------------------------------------------------------ goblins: TacticalAiTurnDriver's loop
        public void GoblinTurn(Fighter goblin)
        {
            var rejects = 0;
            var trace = new List<string>();
            for (var decisionIndex = 0; decisionIndex < 8; decisionIndex++)
            {
                var e = Encounter.Encounter;
                if (e == null || e.CurrentTurn == null || e.CurrentTurn.Id != goblin.Id || Hp(goblin) <= 0) return;

                var decision = goblin.Brain.GetDecision(new TurnContext(goblin.Id, e.GetEnemiesOf(goblin.Id), e.GetAlliesOf(goblin.Id)));
                trace.Add(decision == null ? "null" : $"{decision.Kind}:{decision.Ability?.Id}->{Get(decision.Targets[0]).Spec.Id}(hp{Hp(Get(decision.Targets[0]))},action{(goblin.State.TryGetResource(new DefinitionId(GameDesign.Action), out var act) ? act.Current : -1)})");
                if (decision == null) return;

                if (decision.Kind == BehaviorKind.Move)
                {
                    Movement.MoveToward(goblin.Id, decision.Targets[0], decision.MovementResourceId, decision.StopDistance);
                }
                else
                {
                    var target = Get(decision.Targets[0]);
                    if (target.Hero && Hp(target) <= 0 && Fighters.Any(f => f.Hero && Hp(f) > 0))
                    {
                        farmedDownedHero++;
                        if (Diagnostics.Count < 6)
                            Diagnostics.Add($"seed {Seed}: [{string.Join(" | ", trace)}] {goblin.Spec.Id} decision#{decisionIndex} {decision.Ability?.Id} -> {target.Spec.Id} (hp {Hp(target)}); living heroes: " +
                                            string.Join(",", Fighters.Where(f => f.Hero && Hp(f) > 0).Select(f => $"{f.Spec.Id}@{Distance(goblin, f):F1}m")));
                    }
                    var result = Abilities.Execute(goblin.Id, decision.Ability, decision.Targets);
                    if (!result.Succeeded && ++rejects >= 3) return;
                }

                if (GoblinsWiped() || HeroesAllDown()) return;
            }
        }

        // ------------------------------------------------------------------ heroes: a scripted policy (bonus actions first, then move + Action)
        public void HeroTurn(Fighter hero)
        {
            if (Hp(hero) <= 0)
            {
                if (hero.State.DeathSaves != null) DeathSaves.RollDeathSave(hero.Id);
                return;
            }

            switch (hero.Spec.Id)
            {
                case "bjorn":
                    if (HpFraction(hero) < 0.5f && Has(hero, GameDesign.SecondWindUses)) Cast(hero, GameDesign.SecondWindId, hero);
                    MeleeTurn(hero, "ability-vr-sword-strike", 2f, 1.5f);
                    break;
                case "ulf":
                    if (!hero.State.HasCondition(new DefinitionId(GameDesign.RecklessCondition)) && LivingEnemiesOf(hero).Any(e => Distance(hero, e) <= 8f + 2f))
                        Cast(hero, "ability-vr-reckless-attack", hero);
                    MeleeTurn(hero, "ability-vr-great-axe", 2f, 1.5f);
                    break;
                case "sigrid":
                    RangedTurn(hero, "ability-vr-longbow", 14f, 12f);
                    break;
                case "astrid":
                {
                    var wounded = Fighters.Where(f => f.Hero && HpFraction(f) < 0.5f && Distance(hero, f) <= 8f).OrderBy(HpFraction).FirstOrDefault();
                    if (wounded != null && Has(hero, GameDesign.VerseUses)) Cast(hero, "ability-vr-rallying-verse", wounded);
                    MeleeTurn(hero, "ability-vr-spear-thrust", 2.5f, 2f);
                    break;
                }
            }
        }

        private Fighter Nearest(Fighter from) => LivingEnemiesOf(from).OrderBy(e => Distance(from, e)).FirstOrDefault();
        private Fighter Weakest(Fighter from, float range) => LivingEnemiesOf(from).Where(e => Distance(from, e) <= range).OrderBy(Hp).FirstOrDefault();

        private void MeleeTurn(Fighter hero, string abilityId, float reach, float stop)
        {
            var near = Nearest(hero);
            if (near == null) return;
            if (Distance(hero, near) > reach) Movement.MoveToward(hero.Id, near.Id, movement, stop);
            var target = Weakest(hero, reach);
            if (target != null) Cast(hero, abilityId, target);
        }

        private void RangedTurn(Fighter hero, string abilityId, float range, float stop)
        {
            var near = Nearest(hero);
            if (near == null) return;
            if (Distance(hero, near) > range) Movement.MoveToward(hero.Id, near.Id, movement, stop);
            var target = Weakest(hero, range);
            if (target != null) Cast(hero, abilityId, target);
        }

        // ------------------------------------------------------------------ the fight
        public FightResult Run(int maxRounds = 40)
        {
            Start();
            var stalemate = false;
            while (true)
            {
                var encounter = Encounter.Encounter;
                if (GoblinsWiped() || HeroesAllDown()) break;
                if (encounter.Round > maxRounds) { stalemate = true; break; }

                var current = Get(encounter.CurrentTurn.Id);
                if (current.Hero) HeroTurn(current); else GoblinTurn(current);
                if (GoblinsWiped() || HeroesAllDown()) break;
                Encounter.EndTurn();
            }

            var heroes = Fighters.Where(f => f.Hero).ToList();
            var totalMax = heroes.Sum(MaxHp);
            var lost = heroes.Sum(h => heroesEverDowned.Contains(h.Id) ? MaxHp(h) : MaxHp(h) - Hp(h));
            var result = new FightResult
            {
                Won = !stalemate && GoblinsWiped() && !HeroesAllDown(),
                Stalemate = stalemate,
                Rounds = Encounter.Encounter.Round,
                HeroesDowned = heroesEverDowned.Count,
                FarmedDownedHero = farmedDownedHero,
                HeroHpLost = totalMax == 0 ? 0f : (float)lost / totalMax,
                DamageTakenByHero = new Dictionary<string, int>(damageByHero),
            };
            Encounter.EndCombat();
            return result;
        }
    }
}
