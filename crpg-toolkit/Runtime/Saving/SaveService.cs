using System;
using System.Globalization;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Party;
using CRPG.Quests;
using UnityEngine;

namespace CRPG.Saving
{
    /// <summary>
    /// Converts between live game state and SaveData, and SaveData and its JSON text form. Stateless by
    /// design (a pure transformation, not a long-lived collaborator) -- every method takes the state it needs
    /// as a parameter rather than holding references. Restore expects to be given fresh, empty containers
    /// (a new PartyState/CharacterRegistry/PartyInventoryState/Timeline/QuestLog constructed for the load),
    /// not ones already populated with a live session's state -- PartyState in particular has no "clear"
    /// operation, so restoring into an already-populated one would leave stale members behind.
    /// </summary>
    public static class SaveService
    {
        public static SaveData Capture(PartyState party, ICharacterRegistry characters, PartyInventoryState inventory, ITimeline timeline, QuestLog questLog)
        {
            var data = new SaveData();
            data.Header.FormatVersion = SaveData.CurrentFormatVersion;
            data.Header.SavedAtUtc = DateTime.UtcNow.ToString("o");

            CaptureParty(party, data.Party);
            CaptureCharacters(party, characters, data);
            CaptureInventory(inventory, data);
            CaptureTimeline(timeline, data);
            CaptureQuests(questLog, data);

            return data;
        }

        public static void Restore(SaveData data, PartyState party, CharacterRegistry characters, PartyInventoryState inventory, Timeline timeline, QuestLog questLog)
        {
            RestoreCharacters(data, characters);
            RestoreParty(data.Party, party);
            RestoreInventory(data, inventory);
            RestoreTimeline(data, timeline);
            RestoreQuests(data, questLog);
        }

        public static string ToJson(SaveData data) => JsonUtility.ToJson(data, prettyPrint: true);
        public static SaveData FromJson(string json) => JsonUtility.FromJson<SaveData>(json);

        private static void CaptureParty(PartyState party, PartySaveData data)
        {
            foreach (var member in party.Members) data.MemberIds.Add(member.ToString());
            foreach (var selected in party.Selected) data.SelectedIds.Add(selected.ToString());
            data.ActiveCharacterId = party.ActiveCharacter.ToString();
            foreach (var member in party.Members) data.Groups.Add(new PartyGroupSaveEntry { CharacterId = member.ToString(), GroupId = party.GetGroup(member) });
        }

        private static void RestoreParty(PartySaveData data, PartyState party)
        {
            foreach (var memberId in data.MemberIds) party.AddMember(RuntimeEntityId.FromString(memberId));
            foreach (var entry in data.Groups) party.SetGroup(RuntimeEntityId.FromString(entry.CharacterId), entry.GroupId);

            party.SelectMany(data.SelectedIds.ConvertAll(RuntimeEntityId.FromString));

            if (string.IsNullOrEmpty(data.ActiveCharacterId)) return;
            var active = RuntimeEntityId.FromString(data.ActiveCharacterId);
            if (party.IsSelected(active)) party.SetActive(active);
        }

        private static void CaptureCharacters(PartyState party, ICharacterRegistry characters, SaveData data)
        {
            foreach (var memberId in party.Members)
            {
                if (!characters.TryGetCharacter(memberId, out var character)) continue;

                var entry = new CharacterSaveData { Id = character.Id.ToString() };
                foreach (var pair in character.Stats.AllScores) entry.Stats.Add(new StatScoreSaveEntry { StatId = pair.Key.Value, Score = pair.Value });
                foreach (var pair in character.Resources)
                    entry.Resources.Add(new ResourceSaveEntry { ResourceId = pair.Key.Value, Max = pair.Value.Max, Current = pair.Value.Current });
                foreach (var condition in character.Conditions)
                {
                    entry.Conditions.Add(new ConditionSaveEntry
                    {
                        ConditionId = condition.ConditionId.Value,
                        Source = condition.Source.ToString(),
                        Target = condition.Target.ToString(),
                        HasRemainingDuration = condition.RemainingDuration.HasValue,
                        RemainingDuration = condition.RemainingDuration ?? 0,
                        StackCount = condition.StackCount,
                    });
                }
                foreach (var proficiencyId in character.Proficiencies) entry.Proficiencies.Add(proficiencyId.Value);
                foreach (var featureId in character.Features) entry.Features.Add(featureId.Value);

                data.Characters.Add(entry);
            }
        }

        private static void RestoreCharacters(SaveData data, CharacterRegistry characters)
        {
            foreach (var entry in data.Characters)
            {
                var character = new CharacterState(RuntimeEntityId.FromString(entry.Id));
                foreach (var stat in entry.Stats) character.Stats.SetScore(new DefinitionId(stat.StatId), stat.Score);
                foreach (var resource in entry.Resources)
                    character.AddResource(new ResourceState(new DefinitionId(resource.ResourceId), resource.Max, resource.Current));
                foreach (var condition in entry.Conditions)
                {
                    character.ApplyCondition(new ConditionInstance(
                        new DefinitionId(condition.ConditionId),
                        RuntimeEntityId.FromString(condition.Source),
                        RuntimeEntityId.FromString(condition.Target),
                        condition.HasRemainingDuration ? condition.RemainingDuration : null,
                        condition.StackCount));
                }
                foreach (var proficiencyId in entry.Proficiencies) character.GrantProficiency(new DefinitionId(proficiencyId));
                foreach (var featureId in entry.Features) character.GrantFeature(new DefinitionId(featureId));

                characters.Register(character);
            }
        }

        private static void CaptureInventory(PartyInventoryState inventory, SaveData data)
        {
            foreach (var item in inventory.AllItems)
            {
                var location = inventory.GetLocation(item.InstanceId);
                var entry = new ItemSaveData
                {
                    InstanceId = item.InstanceId.ToString(),
                    DefinitionId = item.DefinitionId.Value,
                    Quantity = item.Quantity,
                    HasCharges = item.Charges.HasValue,
                    Charges = item.Charges ?? 0,
                    EquippedCharacterId = location.IsEquipped ? location.Character.ToString() : null,
                    EquippedSlotId = location.IsEquipped ? location.Slot.Value : null,
                };
                foreach (var pair in item.CustomState) entry.CustomState.Add(new ItemCustomStateSaveEntry { Key = pair.Key, Value = pair.Value });

                data.InventoryItems.Add(entry);
            }
        }

        private static void RestoreInventory(SaveData data, PartyInventoryState inventory)
        {
            foreach (var entry in data.InventoryItems)
            {
                var item = new ItemInstance(
                    RuntimeEntityId.FromString(entry.InstanceId),
                    new DefinitionId(entry.DefinitionId),
                    entry.Quantity,
                    entry.HasCharges ? entry.Charges : null);
                foreach (var custom in entry.CustomState) item.SetCustomState(custom.Key, custom.Value);

                var location = string.IsNullOrEmpty(entry.EquippedCharacterId)
                    ? ItemLocation.SharedInventory
                    : ItemLocation.Equipped(RuntimeEntityId.FromString(entry.EquippedCharacterId), new DefinitionId(entry.EquippedSlotId));

                inventory.AddItem(item, location);
            }
        }

        private static void CaptureTimeline(ITimeline timeline, SaveData data)
        {
            foreach (var timelineEvent in timeline.AllEvents())
            {
                data.TimelineEvents.Add(new TimelineEventSaveData
                {
                    Key = timelineEvent.Key,
                    Sequence = timelineEvent.Sequence,
                    ValueKind = timelineEvent.Value.Kind,
                    ValueNumber = timelineEvent.Value.Number,
                    ValueText = timelineEvent.Value.Text,
                    ValueEntityId = timelineEvent.Value.Entity.ToString(),
                    Amount = timelineEvent.Amount,
                    Source = timelineEvent.Source.ToString(),
                    TimestampUtc = timelineEvent.Timestamp.ToString("o"),
                });
            }
        }

        private static void RestoreTimeline(SaveData data, Timeline timeline)
        {
            foreach (var entry in data.TimelineEvents)
            {
                var value = entry.ValueKind switch
                {
                    TimelineValueKind.Number => TimelineValue.OfNumber(entry.ValueNumber),
                    TimelineValueKind.Text => TimelineValue.OfText(entry.ValueText),
                    TimelineValueKind.EntityRef => TimelineValue.OfEntity(RuntimeEntityId.FromString(entry.ValueEntityId)),
                    _ => TimelineValue.Flag,
                };

                var timestamp = DateTime.Parse(entry.TimestampUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
                timeline.LoadEvent(new TimelineEvent(entry.Key, entry.Sequence, value, entry.Amount, RuntimeEntityId.FromString(entry.Source), timestamp));
            }
        }

        private static void CaptureQuests(QuestLog log, SaveData data)
        {
            foreach (var quest in log.Quests)
            {
                var entry = new QuestSaveData { QuestId = quest.QuestId.Value, Status = quest.Status };
                foreach (var pair in quest.Objectives)
                {
                    entry.Objectives.Add(new QuestObjectiveSaveEntry
                    {
                        ObjectiveId = pair.Key,
                        CurrentCount = pair.Value.CurrentCount,
                        IsComplete = pair.Value.IsComplete,
                    });
                }

                data.Quests.Add(entry);
            }
        }

        private static void RestoreQuests(SaveData data, QuestLog log)
        {
            foreach (var entry in data.Quests)
            {
                var quest = new QuestState(new DefinitionId(entry.QuestId)) { Status = entry.Status };
                foreach (var objective in entry.Objectives)
                {
                    quest.AddObjective(new QuestObjectiveState(objective.ObjectiveId)
                    {
                        CurrentCount = objective.CurrentCount,
                        IsComplete = objective.IsComplete,
                    });
                }

                log.Add(quest);
            }
        }
    }
}
