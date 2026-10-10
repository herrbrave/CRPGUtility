using System;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Presentation;

namespace CRPG.Presentation.Sprites
{
    /// <summary>
    /// Maps the game event bus onto a <see cref="SpriteCueState"/> for one character: <see cref="CombatCueEventBinding"/> (which holds every rule, documented there)
    /// with the sprite cue state's methods as the callbacks. Pure C# (no MonoBehaviour), so it is testable against
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
        private readonly CombatCueEventBinding inner;

        public SpriteCueEventBinding(RuntimeEntityId character, IGameEventBus bus, ICharacterRegistry characters, DefinitionId hitPointResourceId, SpriteCueState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            inner = new CombatCueEventBinding(character, bus, characters, hitPointResourceId, state.PlayHit, state.Down, state.Die, state.Recover);
        }

        public void Dispose() => inner.Dispose();

        /// <summary>See <see cref="CombatCueEventBinding.IsDownAtBind"/>.</summary>
        public static bool IsDownAtBind(RuntimeEntityId character, ICharacterRegistry characters, DefinitionId hitPointResourceId) =>
            CombatCueEventBinding.IsDownAtBind(character, characters, hitPointResourceId);
    }
}
