using System;
using System.Collections.Generic;
using System.Linq;
using CRPG.Core;

namespace CRPG.Party
{
    /// <summary>
    /// The party as controlled during exploration: which characters exist, which are currently selected
    /// (movement/interaction orders apply to these), which one is active, and how members are split into
    /// linked groups for splitting the party.
    /// </summary>
    /// <remarks>
    /// "Selected" and "active" are deliberately different concepts (spec "Active character versus selected
    /// party"): a movement order goes to everyone selected, but only the active character drives the action
    /// bar, skill checks and camera focus. Reuses <see cref="RuntimeEntityId"/> rather than inventing a parallel
    /// "CharacterId" -- a party member already has a runtime entity id.
    /// </remarks>
    public sealed class PartyState
    {
        public const string DefaultGroup = "Default";

        private readonly List<RuntimeEntityId> members = new();
        private readonly HashSet<RuntimeEntityId> selected = new();
        private readonly Dictionary<RuntimeEntityId, string> groupByMember = new();

        public PartyState(IEnumerable<RuntimeEntityId> initialMembers = null)
        {
            if (initialMembers == null) return;
            foreach (var member in initialMembers) AddMember(member);
        }

        public IReadOnlyList<RuntimeEntityId> Members => members;
        public IReadOnlyCollection<RuntimeEntityId> Selected => selected;
        public RuntimeEntityId ActiveCharacter { get; private set; } = RuntimeEntityId.None;

        /// <summary>Task 3.6: fires once per public mutating operation, after invariants hold. Membership and
        /// selection are also driven from outside PartyState's own commands (world click/drag-box via
        /// PartyExplorationController) -- a UI roster panel needs this to stay in sync with world-driven
        /// changes, not just its own button clicks. Raised only from the leaf mutating methods, never from a
        /// thin public wrapper that delegates to one (SelectAll/SelectGroup call SelectMany, which already
        /// raises -- a second raise there would double-notify for one logical operation).</summary>
        public event Action Changed;

        /// <summary>New members join already selected -- exploration's default is the entire party selected.</summary>
        public void AddMember(RuntimeEntityId character)
        {
            if (members.Contains(character)) return;

            members.Add(character);
            groupByMember[character] = DefaultGroup;
            selected.Add(character);

            if (!ActiveCharacter.IsValid) ActiveCharacter = character;
            Changed?.Invoke();
        }

        public void RemoveMember(RuntimeEntityId character)
        {
            if (!members.Remove(character)) return;

            selected.Remove(character);
            groupByMember.Remove(character);

            if (ActiveCharacter == character) ActiveCharacter = FirstSelected();
            Changed?.Invoke();
        }

        public bool IsMember(RuntimeEntityId character) => members.Contains(character);
        public bool IsSelected(RuntimeEntityId character) => selected.Contains(character);

        /// <summary>Click: select exactly this character and make it active.</summary>
        public void Select(RuntimeEntityId character)
        {
            EnsureMember(character);
            selected.Clear();
            selected.Add(character);
            ActiveCharacter = character;
            Changed?.Invoke();
        }

        /// <summary>Drag-box / "select all": select exactly this set. Active moves off only if it fell out of selection.</summary>
        public void SelectMany(IEnumerable<RuntimeEntityId> characters)
        {
            selected.Clear();
            foreach (var character in characters)
            {
                EnsureMember(character);
                selected.Add(character);
            }

            if (!selected.Contains(ActiveCharacter)) ActiveCharacter = FirstSelected();
            Changed?.Invoke();
        }

        public void SelectAll() => SelectMany(members);

        public void SelectGroup(string groupId) => SelectMany(MembersInGroup(groupId));

        /// <summary>Shift-click: toggles membership in the selection without disturbing the rest of it.</summary>
        public void ToggleSelect(RuntimeEntityId character)
        {
            EnsureMember(character);

            if (selected.Remove(character))
            {
                if (ActiveCharacter == character) ActiveCharacter = FirstSelected();
            }
            else
            {
                selected.Add(character);
                if (!selected.Contains(ActiveCharacter)) ActiveCharacter = character;
            }

            Changed?.Invoke();
        }

        public void ClearSelection()
        {
            selected.Clear();
            ActiveCharacter = RuntimeEntityId.None;
            Changed?.Invoke();
        }

        /// <summary>Double-click a portrait: focus a character that's already part of the current selection
        /// without changing what's selected.</summary>
        public void SetActive(RuntimeEntityId character)
        {
            if (!selected.Contains(character))
                throw new InvalidOperationException($"{character} must be selected before it can become the active character.");

            ActiveCharacter = character;
            Changed?.Invoke();
        }

        public void CycleActiveNext() => CycleActive(1);
        public void CycleActivePrevious() => CycleActive(-1);

        /// <summary>Links/unlinks a member into a named group, for splitting the party (default: everyone in one group).</summary>
        public void SetGroup(RuntimeEntityId character, string groupId)
        {
            EnsureMember(character);
            groupByMember[character] = string.IsNullOrEmpty(groupId) ? DefaultGroup : groupId;
            Changed?.Invoke();
        }

        public string GetGroup(RuntimeEntityId character) => groupByMember.TryGetValue(character, out var group) ? group : DefaultGroup;

        public IEnumerable<RuntimeEntityId> MembersInGroup(string groupId) => members.Where(m => GetGroup(m) == groupId);

        private void CycleActive(int direction)
        {
            var orderedSelected = members.Where(selected.Contains).ToList();
            if (orderedSelected.Count == 0) return;

            var currentIndex = orderedSelected.IndexOf(ActiveCharacter);
            if (currentIndex < 0)
            {
                ActiveCharacter = orderedSelected[0];
                Changed?.Invoke();
                return;
            }

            var nextIndex = ((currentIndex + direction) % orderedSelected.Count + orderedSelected.Count) % orderedSelected.Count;
            ActiveCharacter = orderedSelected[nextIndex];
            Changed?.Invoke();
        }

        private RuntimeEntityId FirstSelected()
        {
            foreach (var member in members)
            {
                if (selected.Contains(member)) return member;
            }

            return RuntimeEntityId.None;
        }

        private void EnsureMember(RuntimeEntityId character)
        {
            if (!members.Contains(character))
                throw new InvalidOperationException($"{character} is not a member of this party.");
        }
    }
}
