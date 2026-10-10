using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VikingRaid.Appearance
{
    /// <summary>How an appearance option is put on the master rig.</summary>
    public enum ModelAppearanceKind
    {
        /// <summary>Activate a named child of the master (a body, a skinned hair, a fur); its slot siblings are deactivated.</summary>
        ToggleChild,
        /// <summary>A static mesh prefab (a beard, a helmet, a pouch) cloned under a bone at the stored local pose.</summary>
        AttachStatic,
        /// <summary>A prefab carrying its own skeleton (a cape): cloned whole under a bone at the stored local pose, rigid.</summary>
        AttachOwnRig,
        /// <summary>A prefab that is a whole character with one renderer wanted (a hood): only <c>childName</c> is taken, re-skinned onto the master skeleton by bone ORDER.</summary>
        AttachRigged,
        /// <summary>A material that replaces every Viking-palette material on the character (skin, hair and beard colour, face paint, tunic).</summary>
        Palette,
        /// <summary>A SET of part prefabs (an outfit piece: several regions at once), each bound to the master skeleton BY BONE NAME. Appended after Palette: the kind is serialized as an int.</summary>
        AttachParts,
        /// <summary>Blend shape weights (a body build). Shapes are matched by the name after the region prefix (<c>TORSBlends.defaultBuff</c> is <c>defaultBuff</c>) on every part that has them.</summary>
        BlendShapes,
        /// <summary>Colour overrides for single cells of the look's 32x32 colour map (a skin tone, a hair colour), composed on top of the Palette option's own map. Appended: serialized as an int.</summary>
        ColourCells,
        /// <summary>Like <see cref="AttachParts"/> (bound to the master skeleton by bone name), but the part keeps ITS OWN material (<c>option.material</c>: a Polygon atlas) and is never repainted by a Palette or colour option. Used for Polygon pieces refitted to a Sidekick skeleton (a skinned Viking hair, a pelt, a fur). Appended: serialized as an int.</summary>
        AttachPartsOwnMaterial,
    }

    /// <summary>Which renderers a Palette option repaints.</summary>
    public enum ModelPaletteScope
    {
        /// <summary>Only renderers whose material is in the catalog's palette family (the Viking behaviour; value 0 so an old asset keeps it).</summary>
        FamilyMaterials = 0,
        /// <summary>Every renderer the applier created from a PART SET (the Sidekick parts carry placeholder materials). Rigid pieces from other packs (a Polygon helmet) and weapons and equipment visuals are left alone.</summary>
        AppearanceParts = 1,
    }

    [Serializable]
    public sealed class ModelAppearanceBlendShape
    {
        public string shape;
        public float weight;
    }

    /// <summary>One cell of a colour map (x to the right, y UP from the bottom row, as <c>Texture2D.GetPixels32</c> indexes it) and the colour it takes.</summary>
    [Serializable]
    public sealed class ModelAppearanceCell
    {
        public int x;
        public int y;
        public Color32 colour;
    }

    /// <summary>A rigid piece's displacement under one body build: the offset (in the mount bone's LOCAL axes) it takes at blend shape weight 100; weights below scale it linearly.</summary>
    [Serializable]
    public sealed class ModelAppearanceBuildOffset
    {
        public string shape;
        public Vector3 offset;
    }

    [Serializable]
    public sealed class ModelAppearanceOption
    {
        public string id;
        public string title;
        public ModelAppearanceKind kind;
        /// <summary>ToggleChild: the master child to activate. AttachRigged: the renderer to extract from the prefab.</summary>
        public string childName;
        public GameObject prefab;
        /// <summary>The bone an attachment follows (Head, Spine_03, Hips).</summary>
        public string bone;
        /// <summary>The LOCAL pose under the bone, computed at author time from the rest pose (position zero, rotation = the inverse of the bone's rest world rotation).</summary>
        public Vector3 localPosition;
        public Vector3 localEuler;
        public Vector3 localScale = Vector3.one;
        public Material material;
        /// <summary>A representative colour for the creator's swatch (palettes); alpha 0 means none.</summary>
        public Color32 swatch;
        /// <summary>AttachParts: the part prefabs of this set.</summary>
        public List<GameObject> parts = new();
        /// <summary>BlendShapes: the weights to set (everything else the catalog names is reset to 0).</summary>
        public List<ModelAppearanceBlendShape> blendShapes = new();
        /// <summary>ColourCells: the cells this option overrides in the look's colour map.</summary>
        public List<ModelAppearanceCell> cells = new();
        /// <summary>AttachStatic and AttachOwnRig: how far the piece moves with each body build (a blend shape moves vertices, never bones, so a piece on a bone would otherwise stay put while the body grows through it).</summary>
        public List<ModelAppearanceBuildOffset> buildOffsets = new();
    }

    [Serializable]
    public sealed class ModelAppearanceSlot
    {
        public string id;
        public string title;
        /// <summary>The creator tab this row is on (Body, Head, Wear).</summary>
        public string tab;
        /// <summary>A required slot always has a choice (an unknown or missing id falls back to <see cref="defaultOptionId"/>); an optional one falls back to nothing.</summary>
        public bool required;
        public string defaultOptionId;
        /// <summary>What the creator calls the "nothing chosen" entry of an OPTIONAL row ("From look" for a colour row). Empty: "None".</summary>
        public string noneTitle;
        public List<ModelAppearanceOption> options = new();

        public ModelAppearanceOption Find(string optionId) => string.IsNullOrEmpty(optionId) ? null : options.FirstOrDefault(o => o.id == optionId);
    }

    /// <summary>
    /// The fixed structure of the NPC appearance creator for the Viking master rig: rows (slots) grouped on tabs, each with an ordered list of options and how each is applied. Authored by
    /// <c>ModelAppearanceCatalogAuthor</c> from the Synty pack folders; the ONLY place option ids map to meshes, so renaming a mesh never breaks a saved NPC.
    /// </summary>
    [CreateAssetMenu(menuName = "Viking Raid/Model Appearance Catalog")]
    public sealed class ModelAppearanceCatalog : ScriptableObject
    {
        [SerializeField] private List<ModelAppearanceSlot> slots = new();
        [Tooltip("Every material of the Viking palette family: the Palette option replaces any of these on the character. Renderers with a material outside it (War Camp, Generic parts) keep their own.")]
        [SerializeField] private List<Material> paletteFamily = new();
        [Tooltip("The prefab the appearance is applied to in the creator preview and in the base NPC prefab: the master rig with every body and hair variant on it.")]
        [SerializeField] private GameObject masterPrefab;
        [Tooltip("The family this catalog dresses (shown in the inspector: Viking, Goblin).")]
        [SerializeField] private string family = "Viking";
        [SerializeField] private ModelPaletteScope paletteScope = ModelPaletteScope.FamilyMaterials;
        [Tooltip("The creator tab the preview frames on the head for, and the name of the head bone it frames (Polygon rigs: Head; Sidekick: head).")]
        [SerializeField] private string headTab = "Head";
        [SerializeField] private string headBone = "Head";
        [Tooltip("The idle pose the creator preview holds. Empty: the hero animation profile's idle.")]
        [SerializeField] private AnimationClip previewIdleClip;

        public IReadOnlyList<ModelAppearanceSlot> Slots => slots;
        public IReadOnlyList<Material> PaletteFamily => paletteFamily;
        public GameObject MasterPrefab => masterPrefab;
        public string Family => string.IsNullOrEmpty(family) ? "Viking" : family;
        public ModelPaletteScope PaletteScope => paletteScope;
        public string HeadTab => string.IsNullOrEmpty(headTab) ? "Head" : headTab;
        public string HeadBone => string.IsNullOrEmpty(headBone) ? "Head" : headBone;
        public AnimationClip PreviewIdleClip => previewIdleClip;

        public ModelAppearanceSlot FindSlot(string slotId) => slots.FirstOrDefault(s => s.id == slotId);

        /// <summary>The tab names in the order their first row appears.</summary>
        public IReadOnlyList<string> Tabs => slots.Select(s => s.tab).Distinct().ToList();

        public IEnumerable<ModelAppearanceSlot> SlotsOn(string tab) => slots.Where(s => s.tab == tab);

        public void SetContent(List<ModelAppearanceSlot> newSlots, List<Material> paletteMaterials, GameObject master)
        {
            slots = newSlots; paletteFamily = paletteMaterials; masterPrefab = master;
        }

        /// <summary>Family settings the author sets alongside the content (the defaults are the Viking values).</summary>
        public void SetFamily(string familyName, ModelPaletteScope scope, string tabWithHead, string head, AnimationClip idle)
        {
            family = familyName; paletteScope = scope; headTab = tabWithHead; headBone = head; previewIdleClip = idle;
        }
    }
}
