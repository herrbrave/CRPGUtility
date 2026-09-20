using System;
using System.Collections.Generic;
using CRPG.Core;
using CRPG.UI;
using UnityEngine;
using UnityEngine.UIElements;

namespace BarrowsEnd.Gate
{
    /// <summary>
    /// The gate scene's debug panel (UI Toolkit, built procedurally): acts on the selected party member. Not production UI (Phase 5). Every
    /// button calls one <see cref="GateSessionServices"/> / <see cref="GateBootstrap"/> method, so the same actions are reachable from tests.
    /// Registered with <see cref="UIToolkitPointerCapture"/> so a click on the panel never also becomes a world click.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class GatePanel : MonoBehaviour
    {
        [SerializeField] private GateBootstrap bootstrap;
        [SerializeField] private GateCamera gateCamera;

        private Label status;
        private Label message;
        private UIDocument document;

        private static readonly (string label, string itemId)[] GearButtons =
        {
            ("Helm", "item-iron-helm"), ("Sword", "item-short-sword"), ("Shield", "item-round-shield"), ("Tunic", "item-tunic"),
        };

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
            var panel = new VisualElement { name = "gate-panel" };
            panel.style.position = Position.Absolute;
            panel.style.left = 8;
            panel.style.top = 8;
            panel.style.width = 260;
            panel.style.paddingLeft = panel.style.paddingRight = panel.style.paddingTop = panel.style.paddingBottom = 6;
            panel.style.backgroundColor = new Color(0f, 0f, 0f, 0.7f);
            root.Add(panel);

            status = Text(panel, "");
            message = Text(panel, "");

            Row(panel,
                Button("Attack", () => Act(id => bootstrap.Services.PlayAttack(id))),
                Button("Hit 3-4", () => Act(id => bootstrap.Services.Damage(id, "ability-debug-hit"))),
                Button("Smash", () => Act(id => bootstrap.Services.Damage(id, "ability-debug-smash"))));
            Row(panel,
                Button("Death save", () => Act(id => bootstrap.Services.RollDeathSave(id))),
                Button("Heal & stand", () => Act(id => bootstrap.Services.HealAndStand(id))));

            var gear = new List<Button>();
            foreach (var (label, itemId) in GearButtons)
            {
                var captured = itemId;
                gear.Add(Button(label, () => Act(id => bootstrap.Services.ToggleEquip(id, captured))));
            }

            Row(panel, gear.ToArray());

            Row(panel,
                Button("Door", () => bootstrap.Say(bootstrap.Services.ToggleDoor(bootstrap.DoorObject != null ? bootstrap.DoorObject.transform : null))),
                Button("Save", () => bootstrap.QuickSave()),
                Button("Load", () => bootstrap.QuickLoad()));
            Row(panel,
                Button("Zoom -", () => gateCamera?.ZoomOut()),
                Button("Zoom +", () => gateCamera?.ZoomIn()));
        }

        private void Act(Func<RuntimeEntityId, string> action)
        {
            if (bootstrap == null || !bootstrap.IsReady) return;
            var id = bootstrap.Services.SelectedId;
            if (!id.IsValid)
            {
                bootstrap.Say("select a party member first");
                return;
            }

            bootstrap.Say(action(id));
        }

        private static Label Text(VisualElement parent, string text)
        {
            var label = new Label(text);
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

        private void Update()
        {
            if (status == null || bootstrap == null || !bootstrap.IsReady) return;
            var services = bootstrap.Services;
            var id = services.SelectedId;
            if (!id.IsValid || !services.Session.Characters.TryGetCharacter(id, out var character))
            {
                status.text = "(nobody selected)";
                return;
            }

            var hp = character.TryGetResource(GateSessionServices.HitPoints, out var state) ? $"{state.Current}/{state.Max}" : "-";
            var condition = character.DeathSaves != null ? $" DOWN ({character.DeathSaves.Successes}ok/{character.DeathSaves.Failures}x)"
                : character.HasCondition(GateSessionServices.Unconscious) ? " STABLE" : "";
            status.text = $"HP {hp}{condition}   zoom x{(gateCamera != null ? gateCamera.Scale : 0)}";
        }
    }
}
