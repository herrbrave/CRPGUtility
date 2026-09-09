using CRPG.Actors;
using CRPG.Core;
using CRPG.Dialogue;
using CRPG.Inventory;
using CRPG.Party;
using CRPG.Rules;
using CRPG.UI;
using UnityEngine;
using UnityEngine.UIElements;

// Dogfood-only demo script (Oakhaven demo) -- a one-off bridge, not a general Interaction framework.
// CRPG.Interaction stays an empty stub; a real Interactable/trigger system is future work, out of scope for
// this demo. Runtime dependencies are public settable properties, not a static singleton/service locator --
// whichever bootstrap builds the scene wires them directly after creating each trigger, per the project's
// "no Manager.Instance" composition-root rule.
[RequireComponent(typeof(Collider))]
public sealed class DialogueTriggerZone : MonoBehaviour
{
    [SerializeField] private DialogueDefinition dialogue;

    /// <summary>False (default) for a conversational NPC you can approach repeatedly (Eldor, Griff) -- each
    /// approach is a fresh OnTriggerEnter regardless, so nothing extra is needed for that. True for a one-time
    /// story beat that must not replay on a later approach (e.g. "you descend into the well").</summary>
    [SerializeField] private bool oneShot;

    [SerializeField] private PanelSettings panelSettings;

    public PartyState Party { get; set; }
    public ICharacterRegistry Characters { get; set; }
    public IRulesDatabase RulesDatabase { get; set; }
    public ITimeline Timeline { get; set; }
    public PartyInventoryState Inventory { get; set; }
    public IDiceService DiceService { get; set; }

    /// <summary>The shop this NPC runs, if any -- left null for NPCs that don't sell anything.</summary>
    public StoreInventoryState Store { get; set; }

    /// <summary>Fired once this conversation ends -- the bootstrap uses this to refresh quest progress after
    /// anything that might have just recorded a Timeline event, without this script needing to know about Quests.</summary>
    public event System.Action Completed;

    private bool everTriggered;
    private bool conversationOpen;
    private RuntimeEntityId npcId;

    private void Awake()
    {
        npcId = RuntimeEntityId.NewId();
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (conversationOpen) return;
        if (oneShot && everTriggered) return;
        if (dialogue == null || !other.CompareTag("Player")) return;

        everTriggered = true;
        conversationOpen = true;

        var context = new DialogueContext(npcId, Party, Characters, RulesDatabase, Timeline, Inventory, DiceService, store: Store);

        var uiGo = new GameObject("DialogueUI", typeof(UIDocument));
        var document = uiGo.GetComponent<UIDocument>();
        document.panelSettings = panelSettings;
        var view = uiGo.AddComponent<DialogueView>();
        view.Completed += () =>
        {
            conversationOpen = false;
            Destroy(uiGo);
            Completed?.Invoke();
        };
        view.Initialize(new DialoguePresenter(dialogue, context));
    }
}
