using System.Collections.Generic;
using System.Linq;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;
using VikingRaid.Game;

namespace VikingRaid.Tests
{
    /// <summary>
    /// Plays the heroes' turns through the REAL combat session (the scheduler, the move handler, the end-turn rule): the same policy the Task 2 balance simulation used, driven frame by
    /// frame in real time. Bonus actions before the Action (Second Wind below half HP, Reckless Attack when an enemy is near, Rallying Verse on a hurt ally); then attack what is in
    /// range, else walk toward the nearest enemy; else end the turn. Call <see cref="Tick"/> once per frame.
    /// </summary>
    public sealed class ScriptedParty
    {
        private readonly VikingGame game;
        private RuntimeEntityId turnOwner; private int turnRound = -1;
        private bool bonusDone, moved, attackTried;
        /// <summary>False stops Astrid casting Rallying Verse (tests that need a downed hero to STAY down).</summary>
        public bool HealAllies { get; set; } = true;
        /// <summary>True: every hero just ends his turn (a fight that must last, or a party that must lose).</summary>
        public bool Passive { get; set; }
        public int AbilitiesStarted { get; private set; }
        public int Moves { get; private set; }
        public int TurnsEnded { get; private set; }

        public ScriptedParty(VikingGame game) => this.game = game;

        public void Tick()
        {
            var combat = game.Combat;
            if (!combat.IsActive) return;
            var controller = combat.Controller;
            if (controller.Scheduler.IsBusy) return;
            var encounter = controller.EncounterService.Encounter;
            var current = encounter?.CurrentTurn;
            if (current == null || current.Faction != VikingCombatSession.HeroFaction) return;
            if (!game.Session.Characters.TryGetCharacter(current.Id, out var me) || PartyVitals.IsDown(me)) return;

            if (current.Id != turnOwner || encounter.Round != turnRound) { turnOwner = current.Id; turnRound = encounter.Round; bonusDone = moved = attackTried = false; }

            if (Passive) { TurnsEnded++; controller.EncounterService.EndTurn(); return; }

            var unit = UnitOf(current.Id);
            var enemies = encounter.Combatants.Where(c => c.Faction == VikingCombatSession.GoblinFaction && game.Session.Characters.TryGetCharacter(c.Id, out var s) && !PartyVitals.IsDown(s)).Select(c => c.Id).ToList();
            if (enemies.Count == 0) return;
            var position = game.Session.Characters.TryGetCharacter(current.Id, out _) ? VikingGame.FindAgent(current.Id).transform.position : Vector3.zero;
            var nearest = enemies.OrderBy(e => Vector3.Distance(position, Pos(e))).First();

            // 1. bonus actions before the Action (the Action ends the turn when nothing useful is left)
            if (!bonusDone && Has(me, GameDesign.BonusAction))
            {
                bonusDone = true;
                if (TryBonus(unit, me, current.Id, position, nearest, enemies)) return;
            }

            // 2. the attack
            if (Has(me, GameDesign.Action) && !attackTried)
            {
                var attack = AttackOf(unit);
                var target = enemies.Where(e => Vector3.Distance(position, Pos(e)) <= attack.Range - 0.2f).OrderBy(e => Hp(e)).FirstOrDefault();
                if (target != default)
                {
                    attackTried = true;
                    Start(current.Id, attack.Id, target);
                    return;
                }

                if (!moved && Has(me, GameDesign.Movement))
                {
                    moved = true;
                    var toward = (Pos(nearest) - position); toward.y = 0f;
                    var stop = Mathf.Max(attack.Range * 0.7f, 1.2f);
                    var destination = Pos(nearest) - toward.normalized * stop;
                    if (toward.magnitude > stop) { Moves++; combat.TryHandleMove(new CRPG.Core.MoveCommand(destination, toward.normalized)); return; }
                }
            }

            // 3. nothing left to do
            TurnsEnded++;
            controller.EncounterService.EndTurn();
        }

        private bool TryBonus(string unit, CharacterState me, RuntimeEntityId id, Vector3 position, RuntimeEntityId nearest, List<RuntimeEntityId> enemies)
        {
            switch (unit)
            {
                case "bjorn" when PartyVitals.HpFraction(me) < 0.5f && Has(me, GameDesign.SecondWindUses): Start(id, GameDesign.SecondWindId, id); return true;
                case "ulf" when !me.HasCondition(new DefinitionId(GameDesign.RecklessCondition)) && Vector3.Distance(position, Pos(nearest)) <= 10f: Start(id, "ability-vr-reckless-attack", id); return true;
                case "astrid" when HealAllies && Has(me, GameDesign.VerseUses):
                    var hurt = game.Heroes.Select(h => (h, s: StateOf(h))).Where(x => PartyVitals.HpFraction(x.s) < 0.5f && Vector3.Distance(position, Pos(x.h)) <= 8f).OrderBy(x => PartyVitals.HpFraction(x.s)).FirstOrDefault();
                    if (hurt.h != default) { Start(id, "ability-vr-rallying-verse", hurt.h); return true; }
                    break;
            }

            return false;
        }

        private void Start(RuntimeEntityId source, string abilityId, RuntimeEntityId target)
        {
            game.Session.RulesDatabase.TryResolve<AbilityDefinition>(new DefinitionId(abilityId), out var ability);
            if (game.Combat.Controller.Scheduler.StartAbility(source, ability, new[] { target }) != null) AbilitiesStarted++;
        }

        private (string Id, float Range) AttackOf(string unit) => unit switch
        {
            "bjorn" => ("ability-vr-sword-strike", GameDesign.Ability("ability-vr-sword-strike").Range),
            "ulf" => ("ability-vr-great-axe", GameDesign.Ability("ability-vr-great-axe").Range),
            "sigrid" => ("ability-vr-longbow", GameDesign.Ability("ability-vr-longbow").Range),
            _ => ("ability-vr-spear-thrust", GameDesign.Ability("ability-vr-spear-thrust").Range),
        };

        private string UnitOf(RuntimeEntityId id) => GameDesign.Heroes.ElementAt(game.Heroes.ToList().IndexOf(id)).Id;
        private CharacterState StateOf(RuntimeEntityId id) { game.Session.Characters.TryGetCharacter(id, out var s); return s; }
        private static Vector3 Pos(RuntimeEntityId id) => VikingGame.FindAgent(id)?.transform.position ?? Vector3.zero;
        private int Hp(RuntimeEntityId id) => StateOf(id).TryGetResource(PartyVitals.Hp, out var r) ? r.Current : 0;
        private static bool Has(CharacterState s, string resource) => s.TryGetResource(new DefinitionId(resource), out var r) && r.Current > 0;
    }
}
