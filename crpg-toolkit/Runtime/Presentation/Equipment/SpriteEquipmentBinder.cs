using System;
using System.Collections.Generic;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Presentation.Sprites;
using UnityEngine;

namespace CRPG.Presentation.Equipment
{
    /// <summary>
    /// 2D campaign Task 2.3: makes EquipmentService's ItemEquipped/ItemUnequipped events visually real on a layered sprite
    /// character, the sprite counterpart of <see cref="EquipmentVisualBinder"/> and with the same public contract: identical
    /// <see cref="Initialize"/> is a no-op, a different tuple while bound throws, the subscription starts in OnEnable and is
    /// deliberately kept while disabled, <see cref="Unbind"/> is idempotent and runs on destroy.
    /// <para>It drives a <see cref="SpriteLayerState"/> keyed by INVENTORY slot: a Sprite-kind
    /// <see cref="EquipmentVisualProfile"/> paints the layers it names and hides the layers it lists; unequipping restores
    /// whatever lay beneath. There is no pending-removal machinery: EquipmentService publishes ItemUnequipped(old) then
    /// ItemEquipped(new) synchronously, and the compositor only renders in LateUpdate, so the bare intermediate state is never
    /// presented (a genuine unequip followed by a LATER equip does show a bare frame, by design).</para>
    /// <para>Robustness: the binder holds the <see cref="SpriteLayerCompositor"/> (a Unity object), not just its state, so a
    /// model swap that destroys the compositor is noticed in LateUpdate and the character's gear is reapplied to the new one;
    /// and it reconciles from the inventory every time it starts listening, so events fired while the instance was inactive
    /// (the load path deactivates the instance before Initialize) are never lost.</para>
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public sealed class SpriteEquipmentBinder : MonoBehaviour
    {
        private RuntimeEntityId character;
        private IRulesDatabase rulesDatabase;
        private PartyInventoryState inventory;
        private IGameEventBus eventBus;
        private bool isSubscribed;

        private SpriteLayerCompositor compositor;
        private SpriteLayerState explicitTarget;
        private SpriteLayerState syncedState;
        private bool warnedNoTarget;

        private readonly HashSet<DefinitionId> ownedKeys = new();
        private readonly HashSet<DefinitionId> warnedProfiles = new();

        /// <summary>True between a successful <see cref="Initialize"/> and <see cref="Unbind"/>.</summary>
        public bool IsBound => eventBus != null;

        public void Initialize(RuntimeEntityId character, IRulesDatabase rulesDatabase, PartyInventoryState inventory, IGameEventBus eventBus)
        {
            if (this.eventBus != null)
            {
                if (this.character == character && ReferenceEquals(this.rulesDatabase, rulesDatabase) &&
                    ReferenceEquals(this.inventory, inventory) && ReferenceEquals(this.eventBus, eventBus))
                    return;

                throw new InvalidOperationException(
                    $"{name} is already bound to {this.character} -- call Unbind() before binding to a different character/session.");
            }

            // A None character would reconcile every shared-inventory item (their location Character is None) onto it.
            if (!character.IsValid || rulesDatabase == null || inventory == null || eventBus == null)
            {
                Debug.LogWarning($"[SpriteEquipmentBinder] Cannot bind '{name}': it needs a valid character, rules database, inventory and event bus.", this);
                return;
            }

            this.character = character;
            this.rulesDatabase = rulesDatabase;
            this.inventory = inventory;
            this.eventBus = eventBus;

            Reconcile();
            if (isActiveAndEnabled) Subscribe();
        }

        /// <summary>Detaches: unsubscribes, forgets the binding, and clears exactly the equipment this binder set (appearance is
        /// untouched). Idempotent; called automatically on destroy.</summary>
        public void Unbind()
        {
            Unsubscribe();
            if (syncedState != null) ClearOwned(syncedState);
            ownedKeys.Clear();
            warnedProfiles.Clear();

            character = RuntimeEntityId.None;
            rulesDatabase = null;
            inventory = null;
            eventBus = null;
            syncedState = null;
            warnedNoTarget = false;
        }

        /// <summary>Re-reads the inventory and reapplies every worn item (a profile changed at runtime; a preview refresh).</summary>
        public void Refresh() => Reconcile();

        /// <summary>Explicit target for a consumer with no compositor (a UI preview). Clears this binder's gear on the previous
        /// target and applies it to the new one. Pass null to go back to resolving a compositor.</summary>
        public void SetTarget(SpriteLayerState target)
        {
            explicitTarget = target;
            Reconcile();
        }

        private void OnDestroy() => Unbind();

        // Deliberately keeps the subscription while disabled (same reasoning as EquipmentVisualBinder): the pure state accepts
        // changes at any time, so missing an event would leave worn gear silently wrong once re-enabled.
        private void OnDisable()
        {
        }

        private void OnEnable()
        {
            if (eventBus == null || isSubscribed) return;
            Subscribe();
            Reconcile(); // anything that happened while inactive (the startInactive load path) is picked up here
        }

        private void LateUpdate()
        {
            if (eventBus == null) return;

            var state = ResolveState();
            if (state == null)
            {
                if (!warnedNoTarget)
                {
                    warnedNoTarget = true;
                    Debug.LogWarning($"[SpriteEquipmentBinder] '{name}' has no SpriteLayerCompositor and no explicit target; sprite equipment will not be shown.", this);
                }

                return;
            }

            warnedNoTarget = false;
            if (!ReferenceEquals(state, syncedState)) Reconcile(); // a model swap brought a fresh compositor
        }

        private void Subscribe()
        {
            if (isSubscribed) return;
            eventBus.Subscribe(GameEvent.ItemEquipped, OnItemEquipped);
            eventBus.Subscribe(GameEvent.ItemUnequipped, OnItemUnequipped);
            isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (!isSubscribed) return;
            eventBus?.Unsubscribe(GameEvent.ItemEquipped, OnItemEquipped);
            eventBus?.Unsubscribe(GameEvent.ItemUnequipped, OnItemUnequipped);
            isSubscribed = false;
        }

        private SpriteLayerState ResolveState()
        {
            if (explicitTarget != null) return explicitTarget;
            if (compositor == null) compositor = GetComponentInChildren<SpriteLayerCompositor>(true); // Unity null check: also true once destroyed
            return compositor != null ? compositor.State : null;
        }

        private void OnItemEquipped(GameEventContext context)
        {
            if (context.Target != character || !TryGetSlot(context, out var slot)) return;
            if (!TryGetItemDefinitionId(context, out var itemDefinitionId)) return;

            var state = ResolveState();
            if (state == null) return; // LateUpdate reconciles once a target exists
            Apply(state, slot, itemDefinitionId);
        }

        private void OnItemUnequipped(GameEventContext context)
        {
            if (context.Target != character || !TryGetSlot(context, out var slot)) return;

            var state = ResolveState();
            if (state == null) return;
            ClearSlot(state, slot);
        }

        private void Reconcile()
        {
            if (eventBus == null) return;

            var state = ResolveState();
            if (syncedState != null && !ReferenceEquals(syncedState, state)) ClearOwned(syncedState);
            syncedState = state;
            if (state == null) return;

            ClearOwned(state);
            foreach (var item in inventory.ItemsEquippedBy(character))
                Apply(state, inventory.GetLocation(item.InstanceId).Slot, item.DefinitionId);
        }

        private void Apply(SpriteLayerState state, DefinitionId slot, DefinitionId itemDefinitionId)
        {
            if (!slot.IsValid) return;

            EquipmentVisualProfile profile = null;
            if (rulesDatabase.TryResolve<ItemDefinition>(itemDefinitionId, out var itemDef) && itemDef.EquipmentVisualProfileId.IsValid)
                rulesDatabase.TryResolve(itemDef.EquipmentVisualProfileId, out profile);

            if (profile == null || profile.Kind == EquipmentVisualKind.None)
            {
                ClearSlot(state, slot);
                return;
            }

            if (profile.Kind != EquipmentVisualKind.Sprite)
            {
                if (warnedProfiles.Add(profile.Id))
                    Debug.LogWarning($"[SpriteEquipmentBinder] '{profile.name}' is a {profile.Kind} profile; it draws nothing on a sprite character.", this);
                ClearSlot(state, slot);
                return;
            }

            var paints = new List<LayerPaint>(profile.SpriteLayers.Count);
            foreach (var entry in profile.SpriteLayers)
                if (entry != null) paints.Add(new LayerPaint(entry.layerSlot, new SpriteOption(entry.sprite, entry.tint)));

            state.SetEquipment(slot, profile.SpritePriority, paints, profile.HiddenLayerSlots);
            ownedKeys.Add(slot);
        }

        private void ClearSlot(SpriteLayerState state, DefinitionId slot)
        {
            state.ClearEquipment(slot);
            ownedKeys.Remove(slot);
        }

        private void ClearOwned(SpriteLayerState state)
        {
            foreach (var key in ownedKeys) state.ClearEquipment(key);
            ownedKeys.Clear();
        }

        private static bool TryGetSlot(GameEventContext context, out DefinitionId slot)
        {
            if (context.Data.TryGetValue("SlotId", out var value) && value is DefinitionId id && id.IsValid) { slot = id; return true; }
            slot = default;
            return false;
        }

        private static bool TryGetItemDefinitionId(GameEventContext context, out DefinitionId itemDefinitionId)
        {
            if (context.Data.TryGetValue("ItemDefinitionId", out var value) && value is DefinitionId id) { itemDefinitionId = id; return true; }
            itemDefinitionId = default;
            return false;
        }
    }
}
