using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Presentation.Sprites
{
    /// <summary>
    /// Renders a <see cref="SpriteLayerState"/>: one <see cref="SpriteRenderer"/> per slot (children named
    /// <c>Layer_&lt;slot&gt;</c>, found if already authored, otherwise created), refreshed ONLY in LateUpdate, so an
    /// unequip-then-equip inside one frame is never presented half-done. Sorting is driven from outside through
    /// <see cref="SortingBase"/> (see <see cref="SpriteDepthSorter"/>); the order of the two components' LateUpdates does
    /// not matter.
    /// <para>Sprites need a double-sided (Cull Off) material because at pitch 90 the flat cards face away from the camera.
    /// The default sprite material is: URP's Sprite-Unlit-Default in the editor, the native Sprites/Default in a player.</para>
    /// </summary>
    public sealed class SpriteLayerCompositor : MonoBehaviour
    {
        private static readonly string[] DefaultSlotNames =
        {
            "layer-body", "layer-feet", "layer-clothes", "layer-hair", "layer-helmet", "layer-shield", "layer-weapon",
        };

        [Tooltip("Draw slots, back to front. Leave the default unless the game adds layers (at most 15).")]
        [SerializeField] private List<DefinitionId> slots = DefaultSlots();
        [Tooltip("Parent of the per-slot renderers. Defaults to this transform.")]
        [SerializeField] private Transform layersRoot;
        [Tooltip("Sorting layer the renderers draw on. Must exist in the project (Project Settings > Tags and Layers).")]
        [SerializeField] private string sortingLayerName = SpriteSortingContract.WorldLayer;

        private SpriteLayerState state;
        private SpriteRenderer[] renderers;
        private IReadOnlyList<SpriteRenderer> rendererView = Array.AsReadOnly(Array.Empty<SpriteRenderer>());
        private int appliedVersion = -1;
        private int sortingBase;
        private bool warnedLayer;
        private Color tintMultiplier = Color.white;

        public static List<DefinitionId> DefaultSlots()
        {
            var list = new List<DefinitionId>(DefaultSlotNames.Length);
            foreach (var name in DefaultSlotNames) list.Add(new DefinitionId(name));
            return list;
        }

        /// <summary>True when the configured slots were invalid; the component then renders nothing (see the logged error).</summary>
        public bool Disabled { get; private set; }

        /// <summary>The layer state; built lazily (usable before Awake, e.g. from an editor-time authoring path).</summary>
        public SpriteLayerState State
        {
            get
            {
                if (state == null) BuildState();
                return state;
            }
        }

        public IReadOnlyList<SpriteRenderer> Renderers => rendererView;
        public int AppliedVersion => appliedVersion;

        /// <summary>The transform the per-slot renderers hang under: <c>layersRoot</c> if one was assigned, else this component's own.</summary>
        public Transform LayersRoot => layersRoot != null ? layersRoot : transform;

        /// <summary>A colour multiplied into every layer's tint (default white = no change). Combat cues use it for the hit flash and the
        /// death fade. Setting it re-applies the colours immediately (Apply is otherwise gated on the state version). A multiplier cannot
        /// brighten a sprite, so a "flash" is a tint (e.g. red), not white.</summary>
        public Color TintMultiplier
        {
            get => tintMultiplier;
            set
            {
                if (tintMultiplier == value) return;
                tintMultiplier = value;
                ApplyTints();
            }
        }

        public string SortingLayerName
        {
            get => sortingLayerName;
            set => sortingLayerName = value;
        }

        /// <summary>The band base of this character's sorting order; slot <c>i</c> draws at <c>base + 1 + i</c>. Applied to every
        /// renderer immediately and re-applied after any rebuild.</summary>
        public int SortingBase
        {
            get => sortingBase;
            set
            {
                if (sortingBase == value) return;
                sortingBase = value;
                ApplySorting();
            }
        }

        private void Awake()
        {
            _ = State;
            EnsureRenderers();
        }

        private void LateUpdate()
        {
            if (Disabled) return;
            EnsureRenderers();
            if (state.Version != appliedVersion) Apply();
        }

        private void BuildState()
        {
            var resolved = slots;
            if (resolved == null || resolved.Count == 0)
            {
                Debug.LogWarning($"[SpriteLayerCompositor] No slots configured on '{name}'; using the default layer set.", this);
                resolved = DefaultSlots();
            }

            try
            {
                state = new SpriteLayerState(resolved);
            }
            catch (ArgumentException e)
            {
                Debug.LogError($"[SpriteLayerCompositor] Invalid slot configuration on '{name}': {e.Message} Rendering is disabled.", this);
                Disabled = true;
                state = new SpriteLayerState(DefaultSlots());
            }

            state.Report = message => Debug.LogWarning($"[SpriteLayerCompositor] {message} ({name})", this);
        }

        private void EnsureRenderers()
        {
            if (renderers != null || Disabled) return;

            var root = layersRoot != null ? layersRoot : transform;
            var useLayer = SortingLayerExists(sortingLayerName);
            if (!useLayer && !warnedLayer)
            {
                warnedLayer = true;
                Debug.LogWarning($"[SpriteLayerCompositor] Sorting layer '{sortingLayerName}' does not exist in this project; layers on '{name}' will draw on Default.", this);
            }

            renderers = new SpriteRenderer[State.SlotCount];
            for (var i = 0; i < renderers.Length; i++)
            {
                var childName = "Layer_" + State.SlotAt(i).Value;
                var child = root.Find(childName);
                if (child == null)
                {
                    var go = new GameObject(childName);
                    child = go.transform;
                    child.SetParent(root, false);
                    child.localPosition = Vector3.zero;
                    child.localRotation = Quaternion.identity;
                    child.localScale = Vector3.one;
                }

                var sr = child.GetComponent<SpriteRenderer>();
                if (sr == null) sr = child.gameObject.AddComponent<SpriteRenderer>();
                if (useLayer) sr.sortingLayerName = sortingLayerName;
                renderers[i] = sr;
            }

            rendererView = Array.AsReadOnly(renderers);
            ApplySorting();
        }

        private void Apply()
        {
            var layers = State.Layers;
            for (var i = 0; i < renderers.Length; i++)
            {
                var layer = layers[i];
                var sr = renderers[i];
                sr.sprite = layer.Sprite;
                sr.color = layer.Tint * tintMultiplier;
                sr.enabled = layer.Visible;
            }

            appliedVersion = State.Version;
            ApplySorting();
        }

        private void ApplyTints()
        {
            if (renderers == null || appliedVersion < 0) return;
            var layers = State.Layers;
            for (var i = 0; i < renderers.Length; i++) renderers[i].color = layers[i].Tint * tintMultiplier;
        }

        private void ApplySorting()
        {
            if (renderers == null) return;
            for (var i = 0; i < renderers.Length; i++)
            {
                var order = SpriteSortingContract.SlotOrder(sortingBase, i);
                if (renderers[i].sortingOrder != order) renderers[i].sortingOrder = order;
            }
        }

        // SortingLayer.NameToID returns 0 for an unknown name and IsValid(0) is true (Default has id 0), and a renderer's
        // sortingLayerName reads back "Default" -- so only enumerating the layers can tell a missing layer from Default.
        private static bool SortingLayerExists(string layerName)
        {
            foreach (var layer in SortingLayer.layers)
                if (layer.name == layerName) return true;
            return false;
        }
    }
}
