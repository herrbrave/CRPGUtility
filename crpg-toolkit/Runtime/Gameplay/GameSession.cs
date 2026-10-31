using System;
using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Party;
using CRPG.Quests;
using CRPG.Rules;

namespace CRPG.Gameplay
{
    /// <summary>
    /// Owns one play session's core subsystem state and its lifetime -- session ownership invariant #2
    /// (Docs/Design/Production-Foundation.md sec.3: "The session owns registration, teardown, clock and
    /// command admission"). Party/Characters/Inventory/Timeline/Quests/EventBus are constructed fresh by this
    /// class itself, not passed in, so two sessions can never accidentally share mutable state the way every
    /// existing demo bootstrap's hand-rolled "var registry = new CharacterRegistry(); ..." block risked if two
    /// bootstraps ever ran side by side. RulesDatabase/DiceService ARE passed in -- content and RNG policy are
    /// the composition root's concern (a future ruleset entrypoint/typed registration builder, design sec.3;
    /// not built yet -- see <see cref="GameSessionHost"/>), not something a session should construct itself.
    /// </summary>
    public sealed class GameSession : IDisposable
    {
        public PartyState Party { get; }

        /// <summary>Concrete <see cref="CharacterRegistry"/>, not the read-only <see cref="ICharacterRegistry"/>
        /// seam -- this session owns registration/unregistration (ownership invariant #2), which
        /// <see cref="ICharacterRegistry"/> deliberately doesn't expose to its consumers (Abilities, Combat,
        /// Dialogue, ...). Matches <see cref="CRPG.Saving.SaveService"/>'s own precedent of taking the
        /// concrete type wherever registration, not just lookup, is required.</summary>
        public CharacterRegistry Characters { get; }
        public PartyInventoryState Inventory { get; }
        public ITimeline Timeline { get; }
        public QuestLog Quests { get; }
        public IGameEventBus EventBus { get; }
        public IRulesDatabase RulesDatabase { get; }
        public IDiceService DiceService { get; }

        /// <summary>Task 4.1: the concrete policy instances (IArmorClassRule, ISkillCheckRule, ...) whichever
        /// ruleset provider was used to start this session registered. Empty (never null) for a session started
        /// via the pre-existing <see cref="GameSessionHost.StartSession(IRulesDatabase, IDiceService)"/>
        /// overload, which no composition root has been migrated to populate yet.</summary>
        public PolicyRegistry Policies { get; }

        /// <summary>Task 3.6: every store/loot container this session has ever brought into existence, keyed by
        /// the caller-supplied stable id its owning location authors -- lives here (not on whatever scene
        /// GameObject currently represents it) specifically so a location's state survives that location's
        /// scene being unloaded during travel. Same "outlives any single GameObject" principle
        /// WorldSpawnService's own spawn records already establish for NPCs, extended to store/loot state.
        /// GetOrCreate* construct-and-seed exactly once per id (whatever the caller's seed callback does the
        /// first time an id is requested); every later request -- same visit or a later return -- gets back the
        /// already-existing, possibly-modified instance untouched.</summary>
        private readonly Dictionary<string, StoreInventoryState> stores = new();
        private readonly Dictionary<string, LootContainerState> lootContainers = new();
        public IReadOnlyDictionary<string, StoreInventoryState> Stores => stores;
        public IReadOnlyDictionary<string, LootContainerState> LootContainers => lootContainers;

        /// <summary>Who has asked which NPC to stop its ambient routine (an interaction approaching it, ...).
        /// Per session, so a routine never needs to know about the system that suspends it.</summary>
        public CRPG.Gameplay.Ambient.AmbientSuspension Ambient { get; } = new();

        public bool IsDisposed { get; private set; }

        /// <summary>Whether new player commands should currently be admitted. Real pause/turn-gating is a
        /// later concern (design sec.6/7's action orchestration); for now this is just "the session hasn't
        /// been torn down," the minimum the command-admission ownership invariant requires today.</summary>
        public bool AcceptsCommands => !IsDisposed;

        public GameSession(IRulesDatabase rulesDatabase, IDiceService diceService, PolicyRegistry policies = null)
        {
            RulesDatabase = rulesDatabase ?? throw new ArgumentNullException(nameof(rulesDatabase));
            DiceService = diceService ?? throw new ArgumentNullException(nameof(diceService));
            Policies = policies ?? new PolicyRegistry();

            Party = new PartyState();
            Characters = new CharacterRegistry(rulesDatabase);
            Inventory = new PartyInventoryState();
            Timeline = new Timeline();
            Quests = new QuestLog();
            EventBus = new GameEventBus();
        }

        public StoreInventoryState GetOrCreateStore(string storeId, Action<StoreInventoryState> seedIfNew = null)
        {
            if (stores.TryGetValue(storeId, out var existing)) return existing;

            var store = new StoreInventoryState();
            seedIfNew?.Invoke(store);
            stores[storeId] = store;
            return store;
        }

        public LootContainerState GetOrCreateLootContainer(string containerId, Action<LootContainerState> seedIfNew = null)
        {
            if (lootContainers.TryGetValue(containerId, out var existing)) return existing;

            var container = new LootContainerState();
            seedIfNew?.Invoke(container);
            lootContainers[containerId] = container;
            return container;
        }

        /// <summary>Marks the session torn down. Idempotent. Does not by itself unbind spawned
        /// CharacterAgents/views -- those are owned by whoever spawned them (see
        /// <see cref="WorldSpawnService"/>), which is expected to unbind/destroy its own spawns before or
        /// while disposing the session that owns them (Task 1.3+ builds the composition root that sequences
        /// this properly; this class only owns the "is this session still alive" gate).</summary>
        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
