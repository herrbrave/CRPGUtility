using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Presentation.Equipment
{
    /// <summary>Which kind of visual presentation an <see cref="EquipmentVisualProfile"/> declares --
    /// Docs/Design/Production-Foundation.md sec.4.5's three-way split.</summary>
    public enum EquipmentVisualKind
    {
        /// <summary>Legitimate for invisible/non-worn items -- no GameObject is ever instantiated.</summary>
        None,

        /// <summary>Attaches as a child of a named socket Transform with a fixed local offset -- a weapon, a
        /// sheathed dagger. Works across different Humanoid skeletons through semantic socket naming, not
        /// bone-level binding, so it needs no rig-family check.</summary>
        Rigid,

        /// <summary>Rebinds SkinnedMeshRenderer bones onto the character's own skeleton via an explicit
        /// <see cref="RigBindingMap"/> -- a modular outfit piece that must deform with the body underneath
        /// it.</summary>
        Skinned,

        /// <summary>2D campaign: paints one or more layers of a layered sprite character (a sword paints the weapon
        /// layer, a helmet the helmet layer and hides the hair layer) through <see cref="SpriteEquipmentBinder"/>.
        /// Appended so the serialized values of the three kinds above never change.</summary>
        Sprite,
    }

    /// <summary>One layer a Sprite-kind profile paints.</summary>
    [System.Serializable]
    public sealed class SpriteLayerEntry
    {
        [Tooltip("The layer slot this sprite paints, e.g. layer-weapon or layer-clothes (see SpriteLayerCompositor's slot list).")]
        public DefinitionId layerSlot;

        public Sprite sprite;

        [Tooltip("Multiplied with the sprite. Keep alpha above 0 or the layer is invisible.")]
        public Color tint = Color.white;

        public SpriteLayerEntry() { }

        public SpriteLayerEntry(DefinitionId layerSlot, Sprite sprite, Color tint)
        {
            this.layerSlot = layerSlot;
            this.sprite = sprite;
            this.tint = tint;
        }
    }

    /// <summary>
    /// Task 2.4 (Production Foundation): the data half of "replace bone-name guesswork" -- declares how one
    /// piece of equipment should actually be presented, per Docs/Design/Production-Foundation.md sec.4.5.
    /// Lives in <c>CRPG.Presentation.Equipment</c> rather than alongside <see cref="CRPG.Inventory.ItemDefinition"/>
    /// (which references this only by <see cref="DefinitionId"/>, the same "just a stable tag" pattern it
    /// already uses for <c>GrantedAbilityId</c>/<c>EquippedFeatureId</c>) because binding mechanics are a
    /// presentation concern and the strict dependency direction is Gameplay -&gt; Presentation -&gt;
    /// {Actors, Inventory, Core}, never the reverse -- Inventory must never reference Presentation types
    /// directly.
    /// </summary>
    [CreateAssetMenu(menuName = "CRPG Toolkit/Presentation/Equipment Visual Profile")]
    public sealed class EquipmentVisualProfile : DefinitionAsset
    {
        [SerializeField] private EquipmentVisualKind kind;
        [SerializeField] private GameObject visualPrefab;

        // Body-region hiding (design sec.4.5's "body regions hidden"), reference-counted across garments by
        // EquipmentVisualBinder -- see its own remarks for the full algorithm.
        [SerializeField] private DefinitionId bodyRegionId;
        [SerializeField] private List<DefinitionId> hiddenBodyRegions = new();

        // Rigid attachment
        [SerializeField] private DefinitionId socketId;
        [SerializeField] private Vector3 localPosition;
        [SerializeField] private Quaternion localRotation = Quaternion.identity;
        [SerializeField] private Vector3 localScale = Vector3.one;

        // Skinned attachment. Rig-family compatibility (Task 2.4) is checked against RigBindingMap.RigFamilyId,
        // not a separate field here -- Phase 2 correction pass (R6) removed a profile-level RigFamilyId that
        // was silently never actually checked by EquipmentVisualBinder, creating a false impression of safety.
        // The bone map, not the item profile, is what's genuinely rig-specific.
        [SerializeField] private RigBindingMap boneMap;

        // Sprite attachment (2D campaign). Distinct from the 3D hiddenBodyRegions on purpose: these name LAYER slots.
        [SerializeField] private List<SpriteLayerEntry> spriteLayers = new();
        [Tooltip("Higher wins when two worn items paint the same layer. Convention: body 0, clothes 10, armor 20, helmet 30. Equal priorities fall back to the smaller inventory-slot id, which is deterministic but arbitrary.")]
        [SerializeField] private int spritePriority;
        [SerializeField] private List<DefinitionId> hiddenLayerSlots = new();

        public EquipmentVisualKind Kind => kind;
        public GameObject VisualPrefab => visualPrefab;

        /// <summary>The body region this visual itself physically represents (e.g. "feet" for boots) -- used
        /// so ANOTHER equipped garment's <see cref="HiddenBodyRegions"/> can hide it. DefinitionId.None means
        /// this visual isn't tracked as occupying any region.</summary>
        public DefinitionId BodyRegionId => bodyRegionId;

        /// <summary>Regions this profile hides from view (on whatever OTHER equipped slot currently occupies
        /// them) for as long as it stays equipped.</summary>
        public IReadOnlyList<DefinitionId> HiddenBodyRegions => hiddenBodyRegions;

        public DefinitionId SocketId => socketId;
        public Vector3 LocalPosition => localPosition;
        public Quaternion LocalRotation => localRotation;
        public Vector3 LocalScale => localScale;

        public RigBindingMap BoneMap => boneMap;

        /// <summary>Layers painted by a Sprite-kind profile.</summary>
        public IReadOnlyList<SpriteLayerEntry> SpriteLayers => spriteLayers;

        public int SpritePriority => spritePriority;

        /// <summary>Layer slots hidden (whatever paints them) while this item is worn, e.g. a helmet hiding hair.</summary>
        public IReadOnlyList<DefinitionId> HiddenLayerSlots => hiddenLayerSlots;
    }
}
