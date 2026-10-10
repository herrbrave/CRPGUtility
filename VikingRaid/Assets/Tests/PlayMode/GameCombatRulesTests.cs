using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VikingRaid.Game;

namespace VikingRaid.Tests
{
    /// <summary>The rules the Task 4 review said a live fight gets wrong, each exercised on the real scene: a hero stabilised at 0 HP gets no turn, goblins never hit a downed hero while
    /// another stands, a move that cannot arrive is cancelled, and clicks through the real router and the real ability bar behave.</summary>
    public class GameCombatRulesTests : GameTestBase
    {
        private IEnumerator StartFight(int encounter = 0)
        {
            Time.timeScale = 4f;
            yield return EnterTrigger(encounter);
            yield return WaitUntil(() => Game.State == GameState.Fighting, 40f, "the fight to start");
        }

        /// <summary>Waits for a hero turn that satisfies <paramref name="who"/>; any OTHER hero's turn is ended (nobody is playing them).</summary>
        private IEnumerator WaitForTurnOf(System.Func<RuntimeEntityId, bool> who, float timeout = 60f)
        {
            yield return WaitUntil(() =>
            {
                if (!Game.Combat.IsActive || Game.Combat.CurrentTurnId is not { } id || Game.Combat.Controller.Scheduler.IsBusy) return false;
                if (!Game.Heroes.Contains(id)) return false;
                if (who(id)) return true;
                Game.Combat.Controller.EncounterService.EndTurn();
                return false;
            }, timeout, "the wanted turn");
        }

        [UnityTest]
        public IEnumerator ADownedHeroRollsDeathSavesAndAHeroStabilisedAtZeroNeverGetsATurnNorAFreeWalk()
        {
            yield return StartFight();
            var party = new ScriptedParty(Game) { Passive = true };   // the others just end their turns so the fight lasts; nobody heals, so the hero STAYS down; a downed hero is skipped by the session itself
            var bjorn = Game.Heroes[0];
            // a hero cannot be hit mid-move in real play (his turn only passes once his actions finish): down him between actions, not in the middle of one
            yield return WaitUntil(() => !Game.Combat.Controller.Scheduler.IsBusy, 30f, "the scheduler to go idle");
            var home = Position(bjorn);

            DropToZero(bjorn);
            Game.Session.Characters.TryGetCharacter(bjorn, out var state);
            Assert.IsNotNull(state.DeathSaves, "the real DeathSaveService must set the dying state");
            Assert.IsTrue(state.HasCondition(PartyVitals.Unconscious));

            // 1. dying: his turn passes by itself (a death save is rolled), quickly, with no action
            yield return PassesTurnsOf(party, bjorn, home, 2);

            // 2. STABLE at 0 HP (three successes clear DeathSaves but leave HP 0 and Unconscious): still down, still no turn, no movement
            state.DeathSaves = null;
            Assert.IsTrue(PartyVitals.IsDown(state));
            yield return PassesTurnsOf(party, bjorn, home, 2, checkMoveRefused: true);
            Assert.IsTrue(state.HasCondition(PartyVitals.Unconscious), "the revive sweep must not stand a 0-HP hero up");
        }

        private string AgentInfo(RuntimeEntityId id)
        {
            var a = VikingGame.FindAgent(id); var n = a?.GetComponent<UnityEngine.AI.NavMeshAgent>();
            if (n == null) return "no navmesh agent";
            var near = Game.Combat.Controller.EncounterService.Encounter.Combatants.Where(c => c.Id != id).OrderBy(c => Vector3.Distance(Position(c.Id), a.transform.position)).First();
            return $"agent enabled={n.enabled} prio={n.avoidancePriority} vel={n.velocity.magnitude:F2} hasPath={n.hasPath} stopped={n.isStopped} nearest other {Vector3.Distance(Position(near.Id), a.transform.position):F2} m ({(Game.Heroes.Contains(near.Id) ? "hero" : "goblin")}, prio {Game.GetPriority(near.Id)})";
        }

        private IEnumerator PassesTurnsOf(ScriptedParty party, RuntimeEntityId hero, Vector3 home, int turns, bool checkMoveRefused = false)
        {
            var seen = 0; var inTurn = false; var turnStart = 0f;
            var limit = Time.realtimeSinceStartup + 120f;
            while (seen < turns)
            {
                party.Tick();
                var mine = Game.Combat.IsActive && Game.Combat.CurrentTurnId == hero;
                if (mine && !inTurn) { inTurn = true; turnStart = Time.realtimeSinceStartup; }
                if (mine && checkMoveRefused)
                    Assert.IsNull(Game.Combat.TryHandleMove(new MoveCommand(home + Vector3.back * 4f, Vector3.back)), "a downed hero must not be walkable");
                if (mine) Assert.IsTrue(Game.Combat.Controller.Scheduler.CurrentAction == null || Game.Combat.Controller.Scheduler.CurrentAction.Source != hero, $"a downed hero must never start an action: {Describe()}; action {Game.Combat.Controller.Scheduler.CurrentAction?.GetType().Name} by {(Game.Combat.Controller.Scheduler.CurrentAction?.Source == hero ? "the downed hero" : "someone else")}; seen turns {seen}; hp {(Game.Session.Characters.TryGetCharacter(hero, out var hs) && hs.TryGetResource(PartyVitals.Hp, out var hh) ? hh.Current : -1)}; deathSaves {(hs.DeathSaves == null ? "none" : "set")}");
                if (!mine && inTurn) { inTurn = false; seen++; Assert.Less(Time.realtimeSinceStartup - turnStart, 6f, "the downed hero's turn must pass at once"); }
                if (Vector3.Distance(Position(hero), home) >= 0.5f) Assert.Fail($"a downed hero is lying where he fell (moved {Vector3.Distance(Position(hero), home):F2} m; {Describe()}; {AgentInfo(hero)})");
                if (Game.State != GameState.Fighting) Assert.Fail($"the fight ended before the downed hero had his turns (state {Game.State}; {Describe()}; seen {seen}/{turns})");
                if (Time.realtimeSinceStartup > limit) Assert.Fail("the downed hero never had a turn");
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator GoblinsNeverStrikeADownedHeroWhileAnotherStands()
        {
            yield return StartFight();
            var party = new ScriptedParty(Game) { HealAllies = false };
            var bjorn = Game.Heroes[0];
            var hits = new List<(RuntimeEntityId source, RuntimeEntityId target)>();
            var details = new List<string>();

            // the exact farming scene: a downed hero with a goblin right beside him, the other heroes 10 m away
            yield return WaitUntil(() => !Game.Combat.Controller.Scheduler.IsBusy, 30f, "the scheduler to go idle");
            DropToZero(bjorn);
            Game.Session.EventBus.Subscribe(GameEvent.AfterDamage, ctx =>
            {
                if (ctx.Target != bjorn || ctx.Source == bjorn) return;
                hits.Add((ctx.Source, ctx.Target));
                Game.Session.Characters.TryGetCharacter(bjorn, out var b); b.TryGetResource(PartyVitals.Hp, out var bhp);
                var unit = Game.GoblinsOf(0).Where(g => g.Id == ctx.Source).Select(g => g.UnitId).FirstOrDefault() ?? "?";
                details.Add($"{unit} hit Bjorn (hp now {bhp.Current}, deathSaves {(b.DeathSaves == null ? "none" : b.DeathSaves.Failures + "f/" + b.DeathSaves.Successes + "s")}); goblin at {Vector3.Distance(VikingGame.FindAgent(ctx.Source)?.transform.position ?? Vector3.zero, Position(bjorn)):F1} m; {Describe()}; data[{string.Join(",", ctx.Data.Keys)}]");
            });   // only what happens AFTER he falls counts
            var goblin = Game.GoblinsOf(0)[0].Id;
            Assert.IsTrue(VikingGame.FindAgent(goblin).Motor.Warp(Position(bjorn) + Vector3.right * 1.2f));
            for (var i = 1; i < 4; i++) VikingGame.FindAgent(Game.Heroes[i]).Motor.Warp(Position(bjorn) + new Vector3(-10f, 0f, (i - 2) * 1.5f));

            yield return PlayFight(party, 180f);
            Assert.IsEmpty(hits, "a goblin struck the downed hero while others stood: " + string.Join(" || ", details));
        }

        [UnityTest]
        public IEnumerator AMoveThatCanNeverArriveIsCancelledByTheWatchdogInsteadOfLockingTheFight()
        {
            yield return StartFight();
            yield return WaitForTurnOf(id => Game.Heroes.Contains(id));
            var hero = Game.Combat.CurrentTurnId.Value;
            var goblin = Game.GoblinsOf(0)[0].Id;

            // a mover that is ordered onward but cannot make progress (blocked by a crowd, jittering): freeze the agent after the order so it never moves and never arrives
            var far = Position(hero) - WorldLayout.Arenas[0].Forward * 6f;
            var execution = Game.Combat.Controller.Scheduler.StartMove(hero, VikingGame.FindAgent(hero).Motor, new CRPG.Core.DefinitionId(GameDesign.Movement), far, null);
            Assert.IsNotNull(execution);
            VikingGame.FindAgent(hero).GetComponent<UnityEngine.AI.NavMeshAgent>().isStopped = true;
            var started = Time.realtimeSinceStartup;
            yield return WaitUntil(() => !Game.Combat.Controller.Scheduler.IsBusy, 30f, "the stuck move to end");
            VikingGame.FindAgent(hero).GetComponent<UnityEngine.AI.NavMeshAgent>().isStopped = false;
            Assert.GreaterOrEqual(Game.Combat.WatchdogCancels, 1, $"only the watchdog can end a move that never arrives ({Describe()}; outcome {execution.Outcome}, arrived {execution.ArrivedAtDestination})");
            Assert.Less(Time.realtimeSinceStartup - started, 12f);
        }

        [UnityTest]
        public IEnumerator ClicksThroughTheRealRouterAndBarMoveOnlyTheActiveHeroAttackWhenArmedAndDoNothingOtherwise()
        {
            yield return StartFight();
            yield return WaitForTurnOf(id => Game.Heroes.Contains(id) && id != Game.Heroes[2]);   // a melee hero (not Sigrid)
            var hero = Game.Combat.CurrentTurnId.Value;
            var others = Game.Heroes.Where(h => h != hero).ToDictionary(h => h, Position);

            // (a) a ground click moves only the active hero, and ends
            var forward = WorldLayout.Arenas[0].Forward;
            var before = Position(hero);
            Click(before - forward * 3f);
            Assert.IsTrue(Game.Combat.Controller.Scheduler.IsBusy, "the move should have started");
            yield return WaitUntil(() => !Game.Combat.Controller.Scheduler.IsBusy, 20f, "the move to end");
            Assert.That(Vector3.Distance(before, Position(hero)), Is.InRange(1.5f, 4.5f));
            foreach (var kv in others) Assert.Less(Vector3.Distance(kv.Value, Position(kv.Key)), 0.3f, $"another hero moved {Vector3.Distance(kv.Value, Position(kv.Key)):F2} m (hero {Game.Heroes.ToList().IndexOf(kv.Key)}, prio {Game.GetPriority(kv.Key)}; mover prio {Game.GetPriority(hero)}): a combat click must not drive exploration");

            // (b) press the hero's attack on the real bar, then click an adjacent goblin: the real gate consumes the click and attacks
            var unit = GameDesign.Heroes.ElementAt(Game.Heroes.ToList().IndexOf(hero));
            var attackName = GameDesign.Ability(unit.Abilities.First(a => GameDesign.Ability(a).Kind == AbilityKind.MeleeAttack)).Name;
            var goblin = Game.GoblinsOf(0).Select(g => g.Id).OrderBy(g => Vector3.Distance(Position(g), Position(hero))).First();
            Assert.IsTrue(VikingGame.FindAgent(hero).Motor.Warp(Position(goblin) - forward * 1.4f), "could not put the hero beside the goblin");
            yield return null; yield return null;
            var button = Game.AbilityRegion.Query<Button>().ToList().FirstOrDefault(b => b.text == attackName);
            Assert.IsNotNull(button, $"the bar has no '{attackName}' button: {string.Join(", ", Game.AbilityRegion.Query<Button>().ToList().Select(b => b.text))}");
            UiClick.Submit(button);
            Assert.IsTrue(Game.Combat.TargetingGate.IsArmed, "pressing the ability must arm the targeting gate");
            Game.Session.Characters.TryGetCharacter(hero, out var heroState);
            heroState.TryGetResource(new DefinitionId(GameDesign.Action), out var action);
            Assert.AreEqual(1, action.Current);
            var goblinSpot = Position(goblin);                              // the hit may kill it (and despawn its agent), so remember where it stood
            Click(goblinSpot + Vector3.up * 1f);
            yield return WaitUntil(() => action.Current == 0, 10f, "the attack to spend the Action");

            // (c) with nothing armed a click on a goblin does nothing (and throws nothing)
            yield return WaitUntil(() => !Game.Combat.Controller.Scheduler.IsBusy, 10f, "the attack to finish");
            Assert.IsFalse(Game.Combat.TargetingGate.IsArmed);
            Click(goblinSpot + Vector3.up * 1f);
            Assert.IsFalse(Game.Combat.Controller.Scheduler.IsBusy, "a click on a goblin with nothing armed must not start an action");
            AssertNoErrorsLogged();
        }
    }
}
