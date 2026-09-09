using System.Collections.Generic;
using System.Linq;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Inventory;
using CRPG.Party;
using CRPG.UI;
using UnityEngine;
using UnityEngine.UIElements;

// Dogfood-only demo script (see third_party_assets / TestProject conventions) -- not part of the shipped
// package. Builds a small party from the Fighter sample content, equips them with starter gear, and shows
// the Camp screen (party list + Rest button) so the RestService/CampPresenter/CampView pipeline can be seen
// working end to end.
[RequireComponent(typeof(UIDocument))]
public sealed class CampDemoBootstrap : MonoBehaviour
{
    [SerializeField] private FighterSampleContentBundle content;
    [SerializeField] private string[] partyMemberNames = { "Aldric", "Brynn", "Corwin" };
    [SerializeField] private int[] startingDamageTaken = { 0, 8, 18 }; // so the party doesn't all show full HP

    private void Start()
    {
        if (content == null)
        {
            Debug.LogError("[CampDemoBootstrap] No FighterSampleContentBundle assigned.");
            return;
        }

        var rulesDatabase = new RulesDatabase();
        rulesDatabase.RegisterRange(content.Definitions);

        var registry = new CharacterRegistry();
        var inventory = new PartyInventoryState();
        var buildService = new CharacterBuildService(rulesDatabase);
        var names = new Dictionary<RuntimeEntityId, string>();
        var memberIds = new List<RuntimeEntityId>();

        for (var i = 0; i < partyMemberNames.Length; i++)
        {
            var damageTaken = i < startingDamageTaken.Length ? startingDamageTaken[i] : 0;
            var character = BuildFighter(rulesDatabase, buildService, damageTaken);
            registry.Register(character);
            EquipStarterGear(character, inventory);

            names[character.Id] = partyMemberNames[i];
            memberIds.Add(character.Id);
        }

        var party = new PartyState(memberIds);
        var restService = new RestService(registry, rulesDatabase);
        var presenter = new CampPresenter(party, registry, rulesDatabase, inventory, restService, content.HitPoints, names);

        var view = gameObject.AddComponent<CampView>();
        view.Initialize(presenter);
    }

    private CharacterState BuildFighter(IRulesDatabase rulesDatabase, CharacterBuildService buildService, int damageTaken)
    {
        var plan = new CharacterBuildPlan
        {
            Race = content.Race,
            Class = content.CharacterClass,
            Subclass = content.Subclass,
            Background = content.Background,
            Level = 3,
        };

        plan.AbilityScores.SetScore(content.Strength.Id, 16);
        plan.AbilityScores.SetScore(content.Dexterity.Id, 12);
        plan.AbilityScores.SetScore(content.Constitution.Id, 14);
        plan.AbilityScores.SetScore(content.Intelligence.Id, 10);
        plan.AbilityScores.SetScore(content.Wisdom.Id, 12);
        plan.AbilityScores.SetScore(content.Charisma.Id, 8);

        buildService.RefreshPendingChoices(plan);
        while (plan.PendingChoices.Count > 0)
        {
            var choice = plan.PendingChoices[0];
            plan.ResolveChoice(choice.Id, choice.Options.Take(choice.Count).ToList());
            buildService.RefreshPendingChoices(plan);
        }

        var character = buildService.Build(plan, RuntimeEntityId.NewId());
        character.AddResource(new ResourceState(content.HitPoints.Id, max: 28, current: 28 - damageTaken));
        character.AddResource(new ResourceState(content.Action.Id, max: 1));
        character.AddResource(new ResourceState(content.BonusAction.Id, max: 1));
        character.AddResource(new ResourceState(content.ReactionResource.Id, max: 1));
        return character;
    }

    private void EquipStarterGear(CharacterState character, PartyInventoryState inventory)
    {
        var sword = new ItemInstance(RuntimeEntityId.NewId(), content.Longsword.Id);
        var armor = new ItemInstance(RuntimeEntityId.NewId(), content.ChainShirt.Id);
        inventory.AddItem(sword, ItemLocation.Equipped(character.Id, content.MainHandSlot.Id));
        inventory.AddItem(armor, ItemLocation.Equipped(character.Id, content.ArmorSlot.Id));
    }
}
