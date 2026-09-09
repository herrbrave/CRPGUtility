using UnityEngine;
using UnityEngine.AI;

// Dogfood-only demo script. The well "descends into the cave" by relocating the party rather than loading a
// second scene -- no scene-transition system exists in the toolkit yet, and the approved Oakhaven demo plan
// deliberately keeps everything in one scene rather than building one. Uses NavMeshAgent.Warp so the agent's
// internal state stays consistent (directly setting transform.position would fight the agent next frame).
public sealed class TeleportTriggerZone : MonoBehaviour
{
    [SerializeField] private Transform destination;
    [SerializeField] private bool oneShot = true;

    private bool everTriggered;

    private void Awake() => GetComponent<Collider>().isTrigger = true;

    private void OnTriggerEnter(Collider other)
    {
        if (oneShot && everTriggered) return;
        if (destination == null || !other.CompareTag("Player")) return;

        everTriggered = true;

        if (other.TryGetComponent<NavMeshAgent>(out var agent)) agent.Warp(destination.position);
        else other.transform.position = destination.position;
    }
}
