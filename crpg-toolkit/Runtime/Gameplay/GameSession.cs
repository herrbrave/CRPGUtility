using System;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Party;
using CRPG.Quests;

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

        public bool IsDisposed { get; private set; }

        /// <summary>Whether new player commands should currently be admitted. Real pause/turn-gating is a
        /// later concern (design sec.6/7's action orchestration); for now this is just "the session hasn't
        /// been torn down," the minimum the command-admission ownership invariant requires today.</summary>
        public bool AcceptsCommands => !IsDisposed;

        public GameSession(IRulesDatabase rulesDatabase, IDiceService diceService)
        {
            RulesDatabase = rulesDatabase ?? throw new ArgumentNullException(nameof(rulesDatabase));
            DiceService = diceService ?? throw new ArgumentNullException(nameof(diceService));

            Party = new PartyState();
            Characters = new CharacterRegistry();
            Inventory = new PartyInventoryState();
            Timeline = new Timeline();
            Quests = new QuestLog();
            EventBus = new GameEventBus();
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
