using System;
using CRPG.Actors;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Presentation
{
    /// <summary>
    /// Maps the game event bus onto four presentation callbacks for ONE character -- the rules every presentation (sprite cards, 3D models) shares. Pure C# (no MonoBehaviour), so it is
    /// testable against the REAL damage and death-save machinery. Everything filters on <c>context.Target == character</c>:
    /// <list type="bullet">
    /// <item><c>AfterDamage</c> -> hit (it fires for zero-damage hits too -- the event carries no amount);</item>
    /// <item><c>CreatureDroppedToZeroHP</c> -> down; <c>CreatureDied</c> -> died;</item>
    /// <item><c>CreatureStabilized</c> and <c>ConditionRemoved</c> -> recovered, but ONLY if the character's current HP is above 0. Three successful death saves publish
    /// CreatureStabilized with the character still at 0 HP and unconscious (stable, not up); a natural 20 restores 1 HP first; a heal (with
    /// <c>DeathSaveService</c>'s reviveOnHeal) or a RemoveCondition(unconscious) publishes one of them with HP above 0. The payloads carry nothing else to filter on. No configured
    /// hit-point id, or an unknown character/resource, means never recovered (do not guess).</item>
    /// </list>
    /// Real event orders this must survive: massive damage publishes CreatureDied BEFORE CreatureDroppedToZeroHP (DeathSaveService raises it from inside its own handler), and a killing
    /// blow on an already-down character publishes CreatureDied before AfterDamage -- the consumer treats both as terminal.
    /// <para><paramref name="onContext"/>, when given, is called with the event for hit, down and died (NOT recovered) immediately before the matching callback, in the same exception
    /// guard -- so a game can learn WHO struck (a hit reaction that waits for an arrow to land). A null callback skips both.</para>
    /// <para>The bus has no exception isolation (a throwing subscriber aborts the later ones and faults the effect being applied), so every handler is wrapped, and
    /// <see cref="Dispose"/> sets a flag every handler checks first: unsubscribing does not affect the invocation list the bus already captured for an in-flight publish (in EditMode a
    /// DestroyImmediate runs OnDestroy, hence Dispose, INSIDE the publish being handled).</para>
    /// </summary>
    public sealed class CombatCueEventBinding : IDisposable
    {
        private readonly RuntimeEntityId character;
        private readonly IGameEventBus bus;
        private readonly ICharacterRegistry characters;
        private readonly DefinitionId hitPointResourceId;
        private readonly Action onHit, onDown, onDied, onRecovered;
        private readonly Action<GameEventContext> onContext;
        private bool disposed;

        public CombatCueEventBinding(RuntimeEntityId character, IGameEventBus bus, ICharacterRegistry characters, DefinitionId hitPointResourceId,
            Action onHit, Action onDown, Action onDied, Action onRecovered, Action<GameEventContext> onContext = null)
        {
            this.character = character;
            this.bus = bus ?? throw new ArgumentNullException(nameof(bus));
            this.characters = characters;
            this.hitPointResourceId = hitPointResourceId;
            this.onHit = onHit;
            this.onDown = onDown;
            this.onDied = onDied;
            this.onRecovered = onRecovered;
            this.onContext = onContext;

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

        /// <summary>Whether a character should APPEAR down at the moment its view binds (a save restored while unconscious): it has death-save state, or -- with a configured
        /// hit-point id -- 0 HP (three successful saves leave DeathSaves null, HP 0 and the Unconscious condition applied, so death-save state alone would miss it).</summary>
        public static bool IsDownAtBind(RuntimeEntityId character, ICharacterRegistry characters, DefinitionId hitPointResourceId)
        {
            if (characters == null || !characters.TryGetCharacter(character, out var state)) return false;
            if (state.DeathSaves != null) return true;
            return hitPointResourceId.IsValid && state.TryGetResource(hitPointResourceId, out var hp) && hp.Current <= 0;
        }

        private void OnAfterDamage(GameEventContext context) => Handle(context, onHit, withContext: true);
        private void OnDroppedToZero(GameEventContext context) => Handle(context, onDown, withContext: true);
        private void OnDied(GameEventContext context) => Handle(context, onDied, withContext: true);

        private void OnPossiblyRecovered(GameEventContext context) => Handle(context, onRecovered == null ? null : () =>
        {
            if (HasHitPointsAboveZero()) onRecovered();
        });

        private void Handle(GameEventContext context, Action action, bool withContext = false)
        {
            if (disposed || action == null || context.Target != character) return;

            try
            {
                if (withContext) onContext?.Invoke(context);
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
            return characters.TryGetCharacter(character, out var state)
                   && state.TryGetResource(hitPointResourceId, out var hp)
                   && hp.Current > 0;
        }
    }
}
