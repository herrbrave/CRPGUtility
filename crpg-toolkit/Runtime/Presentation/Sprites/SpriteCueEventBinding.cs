using System;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Presentation.Sprites
{
    /// <summary>
    /// Maps the game event bus onto a <see cref="SpriteCueState"/> for one character. Pure C# (no MonoBehaviour), so it is testable against
    /// the REAL damage and death-save machinery. Everything filters on <c>context.Target == character</c>:
    /// <list type="bullet">
    /// <item><c>AfterDamage</c> -> hit (it fires for zero-damage hits too -- the event carries no amount);</item>
    /// <item><c>CreatureDroppedToZeroHP</c> -> Down; <c>CreatureDied</c> -> Die;</item>
    /// <item><c>CreatureStabilized</c> and <c>ConditionRemoved</c> -> Recover, but ONLY if the character's current HP is above 0. Three
    /// successful death saves publish CreatureStabilized with the character still at 0 HP and unconscious (stable, not up); a natural 20
    /// restores 1 HP first; a heal plus RemoveCondition(unconscious) publishes ConditionRemoved. The payloads carry nothing else to
    /// filter on. No configured hit-point id, or an unknown character/resource, means never Recover (do not guess).</item>
    /// </list>
    /// Real event orders this must survive (all covered by tests): massive damage publishes CreatureDied BEFORE CreatureDroppedToZeroHP
    /// (DeathSaveService raises it from inside its own handler), and a killing blow on an already-down character publishes CreatureDied
    /// before AfterDamage -- Die is terminal, so both end Dead.
    /// <para>The bus has no exception isolation (a throwing subscriber aborts the later ones and faults DamageEffect.Apply), so every handler
    /// is wrapped, and <see cref="Dispose"/> sets a flag every handler checks first: in EditMode a DestroyImmediate runs OnDestroy, hence
    /// Dispose, INSIDE the publish being handled, and unsubscribing does not affect the invocation list the bus already captured.</para>
    /// </summary>
    public sealed class SpriteCueEventBinding : IDisposable
    {
        private readonly RuntimeEntityId character;
        private readonly IGameEventBus bus;
        private readonly ICharacterRegistry characters;
        private readonly DefinitionId hitPointResourceId;
        private readonly SpriteCueState state;
        private bool disposed;

        public SpriteCueEventBinding(RuntimeEntityId character, IGameEventBus bus, ICharacterRegistry characters, DefinitionId hitPointResourceId, SpriteCueState state)
        {
            this.character = character;
            this.bus = bus ?? throw new ArgumentNullException(nameof(bus));
            this.characters = characters;
            this.hitPointResourceId = hitPointResourceId;
            this.state = state ?? throw new ArgumentNullException(nameof(state));

            bus.Subscribe(GameEvent.AfterDamage, OnAfterDamage);
            bus.Subscribe(GameEvent.CreatureDroppedToZeroHP, OnDroppedToZero);
            bus.Subscribe(GameEvent.CreatureDied, OnDied);
            bus.Subscribe(GameEvent.CreatureStabilized, OnPossiblyRecovered);
            bus.Subscribe(GameEvent.ConditionRemoved, OnPossiblyRecovered);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            bus.Unsubscribe(GameEvent.AfterDamage, OnAfterDamage);
            bus.Unsubscribe(GameEvent.CreatureDroppedToZeroHP, OnDroppedToZero);
            bus.Unsubscribe(GameEvent.CreatureDied, OnDied);
            bus.Unsubscribe(GameEvent.CreatureStabilized, OnPossiblyRecovered);
            bus.Unsubscribe(GameEvent.ConditionRemoved, OnPossiblyRecovered);
        }

        /// <summary>Whether a character should APPEAR down at the moment its view binds (a save restored while unconscious): it has
        /// death-save state, or -- with a configured hit-point id -- 0 HP (three successful saves leave DeathSaves null, HP 0 and the
        /// Unconscious condition applied, so death-save state alone would miss it).</summary>
        public static bool IsDownAtBind(RuntimeEntityId character, ICharacterRegistry characters, DefinitionId hitPointResourceId)
        {
            if (characters == null || !characters.TryGetCharacter(character, out var state)) return false;
            if (state.DeathSaves != null) return true;
            return hitPointResourceId.IsValid && state.TryGetResource(hitPointResourceId, out var hp) && hp.Current <= 0;
        }

        private void OnAfterDamage(GameEventContext context) => Handle(context, () => state.PlayHit());
        private void OnDroppedToZero(GameEventContext context) => Handle(context, () => state.Down());
        private void OnDied(GameEventContext context) => Handle(context, () => state.Die());

        private void OnPossiblyRecovered(GameEventContext context) => Handle(context, () =>
        {
            if (HasHitPointsAboveZero()) state.Recover();
        });

        private void Handle(GameEventContext context, Action action)
        {
            if (disposed || context.Target != character) return;

            try
            {
                action();
            }
            catch (Exception ex)
            {
                // Never let a presentation subscriber fault gameplay; and never touch a UnityEngine.Object member from here.
                Debug.LogException(ex);
            }
        }

        private bool HasHitPointsAboveZero()
        {
            if (!hitPointResourceId.IsValid || characters == null) return false;
            return characters.TryGetCharacter(character, out var characterState)
                   && characterState.TryGetResource(hitPointResourceId, out var hp)
                   && hp.Current > 0;
        }
    }
}
