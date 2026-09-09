using System.Collections.Generic;
using System.Linq;
using CRPG.Abilities;
using CRPG.Actors;
using CRPG.AI;
using CRPG.Combat;
using CRPG.Core;
using UnityEngine;
using UnityEngine.UIElements;

// Dogfood-only demo script -- not part of the shipped package. AI-vs-AI, step-by-step combat: two "Heroes"
// and two "Goblins", each side driven by its own UtilityAIDecisionProvider with a differently-tuned
// BehaviorSetDefinition (Heroes finish off the weakest goblin, Goblins pile onto whoever's nearest). Both
// sides also share a "Close Distance" Move Behavior competing on the same utility score as their attack, so
// combatants spawn out of melee range and have to actually walk into it before Strike ever fires. Strike is a
// real attack roll now (1d20 + Strength mod + proficiency vs the target's Dexterity-based AC), so a turn can
// genuinely miss. Mirrors CampDemoBootstrap/StoreDemoBootstrap's "construct everything in Start(), no
// singletons" shape; the on-screen log panel follows CampView's "build UI procedurally in C#" convention.
[RequireComponent(typeof(UIDocument))]
public sealed class AICombatDemoBootstrap : MonoBehaviour
{
    private static readonly DefinitionId HeroesFaction = new("faction-heroes");
    private static readonly DefinitionId GoblinsFaction = new("faction-goblins");

    [SerializeField] private AIDemoContentBundle content;

    private readonly Dictionary<RuntimeEntityId, string> names = new();
    private readonly Dictionary<RuntimeEntityId, CombatantVisual> visuals = new();
    private readonly SceneEntityPositionProvider positions = new();

    private CharacterRegistry registry;
    private CombatEncounterService encounterService;
    private AbilityResolutionService abilityResolution;
    private MovementResolutionService movementResolution;
    private DeathSaveService deathSaveService;
    private UtilityAIDecisionProvider heroBrain;
    private UtilityAIDecisionProvider goblinBrain;
    private bool combatOver;

    private ScrollView logView;
    private Label statusLabel;
    private Button nextTurnButton;

    private void Start()
    {
        if (content == null)
        {
            Debug.LogError("[AICombatDemoBootstrap] No AIDemoContentBundle assigned.");
            return;
        }

        var rulesDatabase = new RulesDatabase();
        rulesDatabase.RegisterRange(content.FighterContent.Definitions);
        rulesDatabase.RegisterRange(content.Definitions);

        registry = new CharacterRegistry();
        var combatLog = new CombatLog();
        var eventBus = new GameEventBus();
        var diceService = new DiceService(new SeededRandomSource(System.Environment.TickCount));
        var aiRandomSource = new SeededRandomSource(System.Environment.TickCount + 1);

        // Str 16/Dex 12 heroes hit harder and more often but are easier to hit; Str 10/Dex 14 goblins are the
        // inverse -- gives the attack-roll math something real to differentiate, not just flat numbers.
        var aldric = SpawnCombatant("Aldric", new Vector3(-2.5f, 0.5f, 0f), strength: 16, dexterity: 12, new Color(0.2f, 0.4f, 0.9f));
        var brynn = SpawnCombatant("Brynn", new Vector3(-2.0f, -0.7f, 0f), strength: 16, dexterity: 12, new Color(0.25f, 0.55f, 0.95f));
        var grik = SpawnCombatant("Grik", new Vector3(2.0f, -0.3f, 0f), strength: 10, dexterity: 14, new Color(0.75f, 0.2f, 0.2f));
        var snarl = SpawnCombatant("Snarl", new Vector3(2.5f, 0.6f, 0f), strength: 10, dexterity: 14, new Color(0.85f, 0.3f, 0.25f));

        eventBus.Subscribe(GameEvent.CreatureDroppedToZeroHP, OnCreatureDropped);
        eventBus.Subscribe(GameEvent.CreatureStabilized, OnCreatureStabilized);
        eventBus.Subscribe(GameEvent.CreatureDied, OnCreatureDied);

        var attackBonusRule = new DefaultAttackBonusRule(registry, rulesDatabase);
        var armorClassRule = new DefaultArmorClassRule(registry, rulesDatabase, content.FighterContent.Dexterity.Id);
        var savingThrowRule = new DefaultSavingThrowRule(registry, rulesDatabase);
        abilityResolution = new AbilityResolutionService(
            registry, diceService, combatLog, eventBus, positions,
            attackBonusRule: attackBonusRule, armorClassRule: armorClassRule, savingThrowRule: savingThrowRule);
        movementResolution = new MovementResolutionService(registry, positions, positions, combatLog);
        deathSaveService = new DeathSaveService(
            registry, diceService, eventBus, content.FighterContent.HitPoints.Id, content.UnconsciousCondition.Id, combatLog);
        heroBrain = new UtilityAIDecisionProvider(content.HeroBehaviorSet, rulesDatabase, registry, abilityResolution, positions, aiRandomSource, combatLog: combatLog);
        goblinBrain = new UtilityAIDecisionProvider(content.GoblinBehaviorSet, rulesDatabase, registry, abilityResolution, positions, aiRandomSource, combatLog: combatLog);

        var turnResources = new TurnResourceSet(new[] { content.FighterContent.Action, content.Movement });
        var initiativeRule = new DefaultInitiativeRule(registry, rulesDatabase, diceService);
        encounterService = new CombatEncounterService(registry, initiativeRule, turnResources, eventBus, combatLog);

        BuildUI();
        combatLog.EntryLogged += OnLogEntry;

        encounterService.StartCombat(new[]
        {
            (aldric, HeroesFaction), (brynn, HeroesFaction), (grik, GoblinsFaction), (snarl, GoblinsFaction),
        });
        RefreshVisuals();
        UpdateStatus();
    }

    private RuntimeEntityId SpawnCombatant(string name, Vector3 position, int strength, int dexterity, Color color)
    {
        var character = new CharacterState(RuntimeEntityId.NewId()) { Level = 3 };
        character.Stats.SetScore(content.FighterContent.Strength.Id, strength);
        character.Stats.SetScore(content.FighterContent.Dexterity.Id, dexterity);
        character.AddResource(new ResourceState(content.FighterContent.HitPoints.Id, max: 20));
        character.AddResource(new ResourceState(content.FighterContent.Action.Id, max: 1));
        character.AddResource(new ResourceState(content.Movement.Id, max: 3));
        registry.Register(character);
        names[character.Id] = name;

        var go = new GameObject(name);
        go.transform.position = position;
        var visual = go.AddComponent<CombatantVisual>();
        visual.Initialize(color, name);
        visuals[character.Id] = visual;
        positions.Register(character.Id, go.transform);

        return character.Id;
    }

    private void BuildUI()
    {
        var document = GetComponent<UIDocument>();
        var root = document.rootVisualElement;
        root.Clear();
        root.style.position = Position.Absolute;
        root.style.top = 0;
        root.style.right = 0;
        root.style.bottom = 0;
        root.style.width = Length.Percent(38);
        root.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
        root.style.paddingLeft = 16;
        root.style.paddingTop = 16;
        root.style.paddingRight = 16;
        root.style.paddingBottom = 16;

        var title = new Label("AI Combat Decision Test");
        title.style.fontSize = 20;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.color = Color.white;
        root.Add(title);

        statusLabel = new Label();
        statusLabel.style.fontSize = 15;
        statusLabel.style.color = Color.white;
        statusLabel.style.marginTop = 8;
        statusLabel.style.marginBottom = 8;
        root.Add(statusLabel);

        nextTurnButton = new Button(OnNextTurnClicked) { text = "Next Turn" };
        nextTurnButton.style.width = 160;
        nextTurnButton.style.height = 32;
        nextTurnButton.style.marginBottom = 12;
        root.Add(nextTurnButton);

        logView = new ScrollView();
        logView.style.flexGrow = 1;
        logView.style.borderTopWidth = 1;
        logView.style.borderTopColor = Color.gray;
        logView.style.paddingTop = 8;
        root.Add(logView);
    }

    private void OnNextTurnClicked()
    {
        if (combatOver) return;

        var current = encounterService.Encounter.CurrentTurn;
        if (current == null) return;

        if (registry.TryGetCharacter(current.Id, out var currentCharacter) && currentCharacter.DeathSaves != null)
        {
            // Unconscious and dying -- incapacitated, no decision to make, just roll the death save.
            deathSaveService.RollDeathSave(current.Id);
        }
        else
        {
            var enemies = encounterService.Encounter.GetEnemiesOf(current.Id);
            var allies = encounterService.Encounter.GetAlliesOf(current.Id);

            var context = new TurnContext(current.Id, enemies, allies);
            var brain = current.Faction == HeroesFaction ? heroBrain : goblinBrain;
            var decision = brain.GetDecision(context);

            if (decision != null)
            {
                if (decision.Kind == BehaviorKind.Move)
                    movementResolution.MoveToward(current.Id, decision.Targets[0], decision.MovementResourceId, decision.StopDistance);
                else
                    abilityResolution.Execute(current.Id, decision.Ability, decision.Targets);
            }
        }

        RefreshVisuals();
        if (CheckWinCondition()) return;

        encounterService.EndTurn();
        RefreshVisuals();
        UpdateStatus();
    }

    private bool CheckWinCondition()
    {
        var combatants = encounterService.Encounter.Combatants;
        var heroesAlive = combatants.Any(c => c.Faction == HeroesFaction);
        var goblinsAlive = combatants.Any(c => c.Faction == GoblinsFaction);
        if (heroesAlive && goblinsAlive) return false;

        combatOver = true;
        nextTurnButton.SetEnabled(false);
        AppendLog(heroesAlive ? "The heroes are victorious!" : "The goblins are victorious!");
        encounterService.EndCombat();
        statusLabel.text = "Combat over.";
        return true;
    }

    private void OnCreatureDropped(GameEventContext context)
    {
        // No longer instant removal -- SRD5.1's real rule is "unconscious, not dead" (DeathSaveService now
        // owns that transition). The combatant stays in the encounter and can still be targeted/finished off.
        if (visuals.TryGetValue(context.Target, out var visual)) visual.SetDefeated();
        AppendLog($"{Friendly(context.Target)} falls unconscious and starts making death saves!");
    }

    private void OnCreatureStabilized(GameEventContext context) => AppendLog($"{Friendly(context.Target)} stabilizes.");

    private void OnCreatureDied(GameEventContext context)
    {
        AppendLog($"{Friendly(context.Target)} dies.");
        encounterService.Encounter.RemoveCombatant(context.Target);
    }

    private void RefreshVisuals()
    {
        foreach (var pair in visuals)
        {
            if (registry.TryGetCharacter(pair.Key, out var character) && character.TryGetResource(content.FighterContent.HitPoints.Id, out var hp))
                pair.Value.SetHpFraction((float)hp.Current / hp.Max);
        }

        var current = encounterService.Encounter?.CurrentTurn;
        foreach (var pair in visuals) pair.Value.SetHighlighted(current != null && current.Id.Equals(pair.Key));
    }

    private void UpdateStatus()
    {
        var current = encounterService.Encounter?.CurrentTurn;
        statusLabel.text = current != null
            ? $"Round {encounterService.Encounter.Round} — {Friendly(current.Id)}'s turn"
            : "Combat ended.";
    }

    private void OnLogEntry(CombatLogEntry entry)
    {
        var text = FormatEntry(entry);
        if (text != null) AppendLog(text);
    }

    private string FormatEntry(CombatLogEntry entry)
    {
        switch (entry)
        {
            case TurnLogEntry turn:
                return $"— Round {turn.Round}: {Friendly(turn.Target)}'s turn —";
            case UtilityDecisionLogEntry decision:
                return decision.Target.IsValid
                    ? $"{Friendly(decision.Source)} chooses {decision.ChosenBehaviorName} on {Friendly(decision.Target)} (score {decision.ChosenScore:F2})"
                    : $"{Friendly(decision.Source)} has nothing worth doing this turn.";
            case MovementLogEntry movement:
                return $"{Friendly(movement.Source)} moves {movement.Distance:F1}m toward {Friendly(movement.Target)}";
            case AttackRollLogEntry attack:
                var outcome = !attack.IsHit ? "Miss." : attack.IsCriticalHit ? "Critical Hit!" : "Hit!";
                return $"{Friendly(attack.Source)} attacks {Friendly(attack.Target)}: {attack.Roll.Total} vs AC {attack.ArmorClass} — {outcome}";
            case DeathSaveLogEntry deathSave:
                return deathSave.NaturalRoll > 0
                    ? $"{Friendly(deathSave.Target)} rolls a death save: {deathSave.NaturalRoll} -- {(deathSave.Success ? "Success" : "Failure")} ({deathSave.Successes} successes, {deathSave.Failures} failures)"
                    : $"{Friendly(deathSave.Target)} takes damage while down -- death save failure ({deathSave.Successes} successes, {deathSave.Failures} failures)";
            case DamageLogEntry damage:
                return $"{Friendly(damage.Target)} takes {damage.Amount} damage ({damage.RemainingAfter} HP left)";
            case DeathLogEntry death:
                return $"{Friendly(death.Target)} is defeated!";
            default:
                return null; // roll/ability-lifecycle noise -- not useful for reading AI decisions
        }
    }

    private void AppendLog(string text)
    {
        var label = new Label(text);
        label.style.whiteSpace = WhiteSpace.Normal;
        label.style.color = Color.white;
        label.style.fontSize = 13;
        label.style.marginBottom = 4;
        logView.Add(label);
        logView.schedule.Execute(() => logView.ScrollTo(label));
    }

    private string Friendly(RuntimeEntityId id) => names.TryGetValue(id, out var name) ? name : id.ToString();
}
