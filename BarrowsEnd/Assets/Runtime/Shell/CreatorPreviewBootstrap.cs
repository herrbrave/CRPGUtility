using System;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Presentation.Creator;
using CRPG.UI;
using UnityEngine;

namespace BarrowsEnd.Shell
{
    /// <summary>
    /// The creator preview scene's composition root (Task 4.2): starts a real ruleset-aware session, maps the two creator JSON files (read through
    /// serialized TextAssets, never by path, so it works in a player) onto a <see cref="CharacterCreatorModel"/>, and shows a
    /// <see cref="CharacterCreatorScreen"/>. Confirm logs the built character; nothing is spawned or registered (that is Task 4.3's job).
    /// It stays as a dev scene so the creator can be opened and clicked through on its own.
    /// </summary>
    public sealed class CreatorPreviewBootstrap : MonoBehaviour
    {
        [SerializeField] private BarrowsEndRulesetProvider ruleset;
        [SerializeField] private TextAsset picks;
        [SerializeField] private TextAsset classTable;
        [SerializeField] private CharacterCreatorScreen screen;
        [SerializeField] private int seed = 4242;

        private GameSessionHost host;

        public GameSession Session { get; private set; }
        public CharacterCreatorModel Model { get; private set; }
        public CreatorResult LastResult { get; private set; }

        private void Start()
        {
            if (ruleset == null || picks == null || classTable == null || screen == null)
                throw new InvalidOperationException("CreatorPreviewBootstrap needs its ruleset, both creator JSON files and the screen (author the scene with 'Barrows End/Content/Author Creator Preview Scene').");

            host = gameObject.AddComponent<GameSessionHost>();
            Session = host.StartSession(ruleset, new DiceService(new SeededRandomSource(seed)), Array.Empty<Type>());
            Model = new CharacterCreatorModel(Session.RulesDatabase, CreatorContentLoader.Build(picks.text, classTable.text), Debug.LogWarning);
            screen.UsePlaceholderStyle = false; // Task 5.3: judge the real skin's look in this dev scene too
            screen.Initialize(Model, OnConfirm, seedSource: () => Environment.TickCount);
        }

        private void OnConfirm(CreatorResult result)
        {
            LastResult = result;
            var character = new CharacterBuildService(Session.RulesDatabase).Build(result.Plan, RuntimeEntityId.NewId());
            Debug.Log($"[CreatorPreview] Built a level {character.Level} {result.Plan.Class.DisplayName} with kit [{string.Join(", ", System.Linq.Enumerable.Select(result.StartingItemIds, i => i.Value))}]; appearance: " +
                      string.Join(", ", System.Linq.Enumerable.Select(character.Appearance, p => $"{p.Key.Value}={p.Value.Value}")));
            screen.Show(); // the dev scene lets you keep experimenting
        }

        private void OnDestroy() => host?.EndSession();
    }
}
