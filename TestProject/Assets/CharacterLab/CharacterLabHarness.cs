using System.Collections;
using System.Collections.Generic;
using CRPG.Core;
using CRPG.Gameplay;
using CRPG.Inventory;
using CRPG.Presentation;
using CRPG.Presentation.Animation;
using CRPG.Presentation.Equipment;
using UnityEngine;
using UnityEngine.AI;

// Dogfood-only tooling (see third_party_assets / TestProject conventions) -- not part of the shipped package.
//
// Gate A's "inspect visuals in PlayMode" acceptance (Docs/Design/Production-Foundation.md sec.8), made
// concrete: press Play, then use the number keys to cycle a staged character through locomotion/turn/action/
// interruption/death/recovery/equip/unequip so a human reviewer can actually look at each pose. Implements
// CRPG.Presentation.ICharacterPreviewAnchor so CharacterSetupWindow's Preview button can find and stage a
// character here without the package itself ever referencing this TestProject-only script.
public sealed class CharacterLabHarness : MonoBehaviour, ICharacterPreviewAnchor
{
    [SerializeField] private Transform anchor;
    [SerializeField] private Transform walkTarget;
    [SerializeField] private ItemDefinition equipItemForTesting;

    // Phase 2 correction pass (R2): equipping "equipItemForTesting" alone silently produced no visual at all,
    // because EquipmentVisualBinder resolves the item's EquipmentVisualProfileId (and, for Skinned, its
    // RigBindingMap) through IRulesDatabase -- a bare DefinitionId tag can't be followed back to the actual
    // asset without also registering it. This harness has no full content-catalog/RulesetDefinition the way a
    // real game would, so the full dependency chain is assigned explicitly here instead.
    [SerializeField] private EquipmentVisualProfile equipItemVisualProfileForTesting;
    [SerializeField] private RigBindingMap equipItemBoneMapForTesting;

    private GameObject current;
    private CharacterAgent agent;
    private CharacterPlayableAnimator animator;
    private EquipmentVisualBinder equipment;
    private NavMeshAgent navAgent;
    private FacingController facing;

    private GameSession session;
    private RulesDatabase rulesDatabase;
    private RuntimeEntityId entityId;
    private bool isEquipped;
    private ItemInstance equippedInstance;

    private readonly List<string> logLines = new();

    public void SpawnCharacter(GameObject wrapperPrefab)
    {
        if (wrapperPrefab == null) return;

        DisposeSession(); // R2: don't leak the previous session when replacing the staged character

        // `current` alone is not a safe guard for "is there already a staged character to remove": it is a
        // non-serialized runtime field, so a C# domain reload (any recompile -- and Preview iterating on
        // CharacterSetupWindow/CharacterWrapperBuilder triggers many) silently resets it to null WITHOUT
        // actually removing the previously staged GameObject, which is still sitting there under the anchor.
        // The next Preview click then saw current == null and skipped the destroy, instantiating a second
        // character on top of the first -- confirmed live: PreviewAnchor ended up with two overlapping
        // BaseHumanMaleCharacter children, one of them a stale copy predating later prefab changes (missing
        // the FacingController added since), which is exactly the "splits into two characters" bug the user
        // saw. The anchor is only ever meant to hold one staged preview character at a time, so clear every
        // existing child directly instead of trusting the cached reference.
        var parent = anchor != null ? anchor : transform;
        for (var i = parent.childCount - 1; i >= 0; i--) DestroyImmediate(parent.GetChild(i).gameObject);

        // Spawn already facing the walk target rather than a fixed world identity rotation. Confirmed live
        // (multiple slow-motion capture sequences) that the previous identity-rotation spawn forced every
        // default Walk through a large (here, exactly 90 degree) reorientation that FacingController, correct
        // as its own turn-rate math is, can't complete before a SHORT demo walk finishes -- for a large
        // fraction of the walk the character visibly faced the camera (its spawn orientation) while actually
        // translating sideways toward the target, which reads exactly as "moonwalking"/backward motion. This
        // is not a bug in FacingController or the movement system (both behave correctly; verified via clip
        // speed sign, clip-time-vs-walltime correlation, and this rotation trace itself) -- it is this demo's
        // own spawn-vs-target geometry making an ordinary, otherwise-brief turn-catch-up transient dominate a
        // walk that is barely longer than the turn itself. Pre-aligning spawn facing removes the mismatch for
        // the demo's first, most-scrutinized walk; Turn (key 3) remains available to exercise the turn/
        // facing-priority behavior deliberately.
        var spawnRotation = Quaternion.identity;
        if (walkTarget != null)
        {
            var toTarget = walkTarget.position - parent.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.0001f) spawnRotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        }

        var instance = Instantiate(wrapperPrefab, parent.position, spawnRotation, parent);
        instance.name = wrapperPrefab.name;
        RebindTo(instance);

        if (Application.isPlaying) BindForPlay();
        Log($"Staged '{wrapperPrefab.name}'.");
    }

    private void RebindTo(GameObject characterGo)
    {
        current = characterGo;
        agent = current.GetComponent<CharacterAgent>();
        animator = current.GetComponent<CharacterPlayableAnimator>();
        equipment = current.GetComponent<EquipmentVisualBinder>();
        navAgent = current.GetComponent<NavMeshAgent>();
        facing = current.GetComponent<FacingController>();
        isEquipped = false;
        equippedInstance = null;
    }

    private void Start()
    {
        Log("CharacterLab ready. 1=Idle/Stop 2=Walk 3=Turn 4=Action 5=Interrupt 6=Death 7=Recover 8=Equip 9=Unequip");

        // R2: the Preview button can stage a character and save the scene BEFORE Play ever starts. current/
        // agent/etc. are deliberately non-serialized runtime fields (they hold live component references,
        // which is correct), so after a scene reload they read back as null even though the actual staged
        // character GameObject is still sitting there under the SERIALIZED anchor. Without this, Start()
        // never rediscovers it, and every control silently no-ops forever (confirmed via a live saved-scene
        // probe: anchor had one child, but this component's own `current` was null).
        if (current == null)
        {
            var parent = anchor != null ? anchor : transform;
            if (parent.childCount > 0) RebindTo(parent.GetChild(0).gameObject);
        }

        if (current != null && agent != null && !agent.IsBound) BindForPlay();
    }

    // The Preview button (Editor, not Play) can stage a character before Play starts -- the actual
    // session/identity binding (Initialize, matching WorldSpawnService's own wiring) only makes sense once a
    // real Play session exists to own it.
    private void BindForPlay()
    {
        rulesDatabase = new RulesDatabase();
        session = new GameSession(rulesDatabase, new DiceService(new SeededRandomSource(1)));
        entityId = RuntimeEntityId.NewId();

        agent.Bind(entityId, session);
        animator?.Initialize(entityId, session.EventBus);
        equipment?.Initialize(entityId, rulesDatabase, session.Inventory, session.EventBus);

        if (equipItemForTesting != null) rulesDatabase.Register(equipItemForTesting);
        if (equipItemVisualProfileForTesting != null) rulesDatabase.Register(equipItemVisualProfileForTesting);
        if (equipItemBoneMapForTesting != null) rulesDatabase.Register(equipItemBoneMapForTesting);
    }

    private void DisposeSession()
    {
        session?.Dispose();
        session = null;
        rulesDatabase = null;
        isEquipped = false;
        equippedInstance = null;
    }

    private void OnDestroy() => DisposeSession();

    private void Update()
    {
        if (current == null) return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) StopWalking();
        if (Input.GetKeyDown(KeyCode.Alpha2)) Walk();
        if (Input.GetKeyDown(KeyCode.Alpha3)) Turn();
        if (Input.GetKeyDown(KeyCode.Alpha4)) Action();
        if (Input.GetKeyDown(KeyCode.Alpha5)) StartCoroutine(Interrupt());
        if (Input.GetKeyDown(KeyCode.Alpha6)) Death();
        if (Input.GetKeyDown(KeyCode.Alpha7)) Recover();
        if (Input.GetKeyDown(KeyCode.Alpha8)) Equip();
        if (Input.GetKeyDown(KeyCode.Alpha9)) Unequip();
    }

    private void StopWalking()
    {
        agent?.Motor?.Stop();
        // R2: Turn's manual facing request was never released, so it could keep overriding motion-driven
        // facing indefinitely after the turn exercise. Idle/Stop is this harness's natural "return to
        // neutral" control, so it also releases any outstanding manual turn override.
        facing?.ClearFacing(FacingPriority.Interaction);
        Log("Idle: stopped movement and released any manual turn override.");
    }

    private void Walk()
    {
        if (agent?.Motor == null) { Log("No motor bound."); return; }

        // Turn()'s manual facing request sits at FacingPriority.Interaction, which outranks the automatic
        // Motion-priority facing FacingController applies while actually moving -- confirmed live: pressing
        // Turn then Walk (a completely natural order to try the controls in) left the character's facing
        // permanently locked wherever Turn pointed it, sliding toward the destination without ever turning
        // to face it. Previously only Idle/Stop released this override; a fresh Walk request is just as much
        // a "the manual turn no longer applies" signal, so it releases it too.
        facing?.ClearFacing(FacingPriority.Interaction);

        var destination = walkTarget != null ? walkTarget.position : current.transform.position + current.transform.forward * 5f;
        var result = agent.Motor.SetDestination(destination);
        Log($"Walk: SetDestination -> {result.Status}.");
    }

    private void Turn()
    {
        if (facing == null) { Log("No FacingController on this wrapper."); return; }
        var away = -current.transform.forward;
        facing.RequestFacing(FacingPriority.Interaction, away);
        Log("Turn: requested facing away from current forward.");
    }

    private void Action()
    {
        if (animator == null) { Log("No CharacterPlayableAnimator bound."); return; }
        var token = animator.RequestAction(new DefinitionId("anim-attack-melee"), outcome => Log($"Action outcome: {outcome}."));
        Log(token.IsValid ? "Action: requested anim-attack-melee." : "Action: rejected (terminal, or cue not authored on this profile).");
    }

    private IEnumerator Interrupt()
    {
        if (animator == null) { Log("No CharacterPlayableAnimator bound."); yield break; }
        Log("Interrupt: requesting anim-attack-melee, then interrupting it mid-swing with a second request.");
        animator.RequestAction(new DefinitionId("anim-attack-melee"), outcome => Log($"First action outcome: {outcome}."));
        yield return new WaitForSeconds(0.05f);
        animator.RequestAction(new DefinitionId("anim-attack-melee"), outcome => Log($"Interrupting action outcome: {outcome}."));
    }

    private void Death()
    {
        if (animator == null) { Log("No CharacterPlayableAnimator bound."); return; }
        animator.RequestTerminal(new DefinitionId("anim-death"));
        Log("Death: entered terminal pose (no 'anim-death' clip is authored yet in the shipped fixture profile -- the pose freezes at its last baseline, but the STATE change -- rejecting further move/action/stance requests -- is real; author a death clip to see the pose itself).");
    }

    private void Recover()
    {
        if (animator == null) { Log("No CharacterPlayableAnimator bound."); return; }
        animator.Recover();
        Log("Recover: cleared terminal state.");
    }

    private void Equip()
    {
        if (equipItemForTesting == null) { Log("No 'Equip Item For Testing' assigned in the Inspector."); return; }
        if (session == null) { Log("Not bound to a Play session yet."); return; }
        if (isEquipped) { Log("Already equipped."); return; }

        equippedInstance = new ItemInstance(RuntimeEntityId.NewId(), equipItemForTesting.Id);
        session.Inventory.AddItem(equippedInstance);
        var equipmentService = new EquipmentService(session.Characters, rulesDatabase, session.Inventory, session.EventBus);
        equipmentService.Equip(equippedInstance.InstanceId, entityId, equipItemForTesting.EquipmentSlotId);
        isEquipped = true;
        Log($"Equipped '{equipItemForTesting.DisplayName}'.");
    }

    private void Unequip()
    {
        if (!isEquipped || equippedInstance == null) { Log("Nothing equipped."); return; }
        var equipmentService = new EquipmentService(session.Characters, rulesDatabase, session.Inventory, session.EventBus);
        equipmentService.Unequip(equippedInstance.InstanceId);
        isEquipped = false;
        Log("Unequipped.");
    }

    private void Log(string message)
    {
        Debug.Log($"[CharacterLab] {message}");
        logLines.Add(message);
        if (logLines.Count > 8) logLines.RemoveAt(0);
    }

    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 520, 220), GUI.skin.box);
        GUILayout.Label("Character Lab -- Gate A visual inspection", BoldLabelStyle());
        GUILayout.Label("1 Idle/Stop  2 Walk  3 Turn  4 Action  5 Interrupt  6 Death  7 Recover  8 Equip  9 Unequip");
        GUILayout.Space(6);
        foreach (var line in logLines) GUILayout.Label(line);
        GUILayout.EndArea();
    }

    private static GUIStyle BoldLabelStyle() => new(GUI.skin.label) { fontStyle = FontStyle.Bold };
}
