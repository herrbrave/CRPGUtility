using System;
using CRPG.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace BarrowsEnd.Shell
{
    /// <summary>A minimal in-game panel for the Phase 3 shell (not the Phase 5 HUD): where the party is, the last message, save / load and zoom. Registered with
    /// <see cref="UIToolkitPointerCapture"/> so a click on it never also becomes a world click.</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class ShellPanel : MonoBehaviour
    {
        [SerializeField] private ShellBootstrap bootstrap;

        private Label status;
        private Label message;
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

        private void Update()
        {
            if (status == null || bootstrap == null || !bootstrap.IsReady) return;
            var scene = SceneManager.GetActiveScene();
            status.text = $"{scene.name}   zoom x{bootstrap.CameraRig.Rig.Scale}";
        }
    }
}
