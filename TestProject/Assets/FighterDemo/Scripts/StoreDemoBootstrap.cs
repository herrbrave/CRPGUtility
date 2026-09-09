using CRPG.Core;
using CRPG.Inventory;
using CRPG.UI;
using UnityEngine;
using UnityEngine.UIElements;

// Dogfood-only demo script (see third_party_assets / TestProject conventions) -- not part of the shipped
// package. Seeds a starting purse and a spare Longsword to sell, stocks a merchant selling Healing Potions
// and gear, and shows the Store screen so the StoreService/StorePresenter/StoreView pipeline can be seen
// working end to end. Needs no CharacterState/PartyState at all -- buying and selling only touch the shared
// PartyInventoryState, which is the whole point of the party having one inventory instead of four backpacks.
[RequireComponent(typeof(UIDocument))]
public sealed class StoreDemoBootstrap : MonoBehaviour
{
    [SerializeField] private FighterSampleContentBundle content;
    [SerializeField] private int startingGold = 200;

    private void Start()
    {
        if (content == null)
        {
            Debug.LogError("[StoreDemoBootstrap] No FighterSampleContentBundle assigned.");
            return;
        }

        var rulesDatabase = new RulesDatabase();
        rulesDatabase.RegisterRange(content.Definitions);

        var inventory = new PartyInventoryState();
        inventory.AddItem(new ItemInstance(RuntimeEntityId.NewId(), content.GoldPiece.Id, startingGold));
        inventory.AddItem(new ItemInstance(RuntimeEntityId.NewId(), content.Longsword.Id)); // a spare, to demonstrate selling

        var shop = new StoreInventoryState(buyPriceMultiplier: 1f, sellPriceMultiplier: 0.5f);
        shop.AddListing(content.HealingPotion.Id, quantity: 5);
        shop.AddListing(content.Longsword.Id);
        shop.AddListing(content.ChainShirt.Id);

        var storeService = new StoreService(rulesDatabase, inventory, content.GoldPiece.Id);
        var presenter = new StorePresenter(shop, inventory, rulesDatabase, storeService);

        var view = gameObject.AddComponent<StoreView>();
        view.Initialize(presenter);
    }
}
