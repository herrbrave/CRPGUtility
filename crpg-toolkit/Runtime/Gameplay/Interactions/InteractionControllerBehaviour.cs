using CRPG.Core;
using CRPG.Party;
using UnityEngine;

namespace CRPG.Gameplay.Interactions
{
    /// <summary>MonoBehaviour host for <see cref="InteractionController"/> -- mirrors the established
    /// "MonoBehaviour hosts a plain-C# class" split (CharacterAgent/GameSessionHost, PartyCameraFollow/
    /// PartyCameraFollowBehaviour).</summary>
    public sealed class InteractionControllerBehaviour : MonoBehaviour
    {
        private InteractionController controller;

        public InteractionController Controller => controller;

        public void Initialize(IPlayerCommandSource commandSource, PartyState party, GameSession session)
        {
            controller?.Dispose();
            controller = new InteractionController(commandSource, party, session);
        }

        private void Update() => controller?.Tick(Time.deltaTime);

        // A disabled host stops receiving Update() ticks -- an in-flight request must not freeze silently, it
        // must actually end (mirrors the project's repeated "a paused/disabled component must not leave stale
        // bookkeeping" lesson).
        private void OnDisable() => controller?.Cancel();

        private void OnDestroy() => controller?.Dispose();
    }
}
