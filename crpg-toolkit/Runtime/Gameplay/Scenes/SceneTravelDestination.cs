using UnityEngine;

namespace CRPG.Gameplay.Scenes
{
    /// <summary>One entry point into a location scene, authored once per destination id a TravelInteractionAdapter
    /// elsewhere might target. Found via FindObjectsByType in the freshly (additively) loaded, still-inactive
    /// scene -- the counterpart of WorldInteractable's own anchor, but for scene travel rather than in-scene
    /// interaction.</summary>
    public sealed class SceneTravelDestination : MonoBehaviour
    {
        [SerializeField] private string destinationId;

        public string DestinationId => destinationId;
        public Transform Anchor => transform;
    }
}
