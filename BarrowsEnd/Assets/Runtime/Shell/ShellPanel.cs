using System;
using System.Linq;
using BarrowsEnd.Gate;
using CRPG.Actors;
using CRPG.Core;
using CRPG.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace BarrowsEnd.Shell
{
    /// <summary>The Phase 3/4 shell's placeholder debug panel (party/message/save/load/zoom) -- Task 5.1 replaced it in the shipped
    /// Core scene with the real HUD (<see cref="HudBootstrap"/>). The type itself is kept only for its static <see cref="Summarize"/>
    /// method, which three EditMode tests call directly and which the real HUD's own status line also uses; nothing instantiates
    /// this MonoBehaviour in the authored scene anymore. Registered with <see cref="UIToolkitPointerCapture"/> so a click on it
    /// never also becomes a world click, for anyone still using it as a standalone dev panel.</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ShellPanel : MonoBehaviour
    {
        [SerializeField] private ShellBootstrap bootstrap;

        private Label status;
        private Label message;
        private Label summary;
        private UIDocument document;

        private void OnEnable()
        {
            document = GetComponent<UIDocument>();
            UIToolkitPointerCapture.Register(document);
            Build(document.rootVisualElement);
            if (bootstrap != null) bootstrap.MessageChanged += OnMessage;
        }

        private void OnDisable()
        {
            if (bootstrap != null) bootstrap.MessageChanged -= OnMessage;
            if (document != null) UIToolkitPointerCapture.Unregister(document);
        }

        private void OnMessage(string text)
        {
            if (message != null) message.text = text;
        }

        private void Build(VisualElement root)
        {
            root.Clear();
            var panel = new VisualElement { name = "shell-panel" };
            panel.style.position = Position.Absolute;
            panel.style.left = 8;
            panel.style.top = 8;
            panel.style.width = 240;
            panel.style.paddingLeft = panel.style.paddingRight = panel.style.paddingTop = panel.style.paddingBottom = 6;
            panel.style.backgroundColor = new Color(0f, 0f, 0f, 0.7f);
            root.Add(panel);

            status = Text(panel);
            message = Text(panel);
            summary = Text(panel);
            summary.name = "shell-summary";
            Row(panel, Button("Save", () => bootstrap.SaveNow()), Button("Load", () => bootstrap.LoadNow()));
            Row(panel,
                Button("Zoom -", () => bootstrap.CameraRig.Rig.ApplyZoom(-1f)),
                Button("Zoom +", () => bootstrap.CameraRig.Rig.ApplyZoom(1f)));
        }

        private static Label Text(VisualElement parent)
        {
            var label = new Label();
            label.style.color = Color.white;
            label.style.whiteSpace = WhiteSpace.Normal;
            parent.Add(label);
            return label;
        }

        private static Button Button(string text, Action onClick) => new(onClick) { text = text };

        private static void Row(VisualElement parent, params Button[] buttons)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            foreach (var button in buttons) row.Add(button);
            parent.Add(row);
        }

        /// <summary>The selected member in one line (Task 4.3, what Gate D reads): hit points, the number of proficiencies and the names of the granted features. There is no
        /// class-identity field on a character, so the class shows through its features and hit points, which differ by class.</summary>
        public static string Summarize(CharacterState character, IRulesDatabase rules)
        {
            if (character == null) return "";
            var hp = character.TryGetResource(GateSessionServices.HitPoints, out var resource) ? $"HP {resource.Current}/{resource.Max}" : "HP -";
            var names = character.Features
                .Select(f => rules != null && rules.TryResolve<FeatureDefinition>(f, out var definition) && !string.IsNullOrEmpty(definition.DisplayName) ? definition.DisplayName : null)
                .Where(n => n != null)
                .Distinct();
            return $"{hp}   Proficiencies: {character.Proficiencies.Count}   Features: {string.Join(", ", names)}";
        }

        private void Update()
        {
            if (status == null || bootstrap == null || !bootstrap.IsReady) return;
            var scene = SceneManager.GetActiveScene();
            status.text = $"{scene.name}   zoom x{bootstrap.CameraRig.Rig.Scale}";
            var session = bootstrap.Services?.Session;
            summary.text = session != null && session.Characters.TryGetCharacter(session.Party.ActiveCharacter, out var member) ? Summarize(member, session.RulesDatabase) : "";
        }
    }
}
