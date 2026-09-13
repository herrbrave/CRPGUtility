using UnityEngine;

namespace CRPG.Presentation
{
    /// <summary>
    /// Task 2.5 (Production Foundation): implemented by a scene-side harness (e.g. TestProject's
    /// dogfood-only CharacterLabHarness) that CharacterSetupWindow's Preview button can stage a freshly-built
    /// wrapper into. The package must never hold a compile-time reference to TestProject-only content -- this
    /// interface is how CharacterSetupWindow finds a harness at runtime (via a type search, not a direct type
    /// reference) without ever knowing that consumer content exists.
    /// </summary>
    public interface ICharacterPreviewAnchor
    {
        /// <summary>Replaces whatever character is currently staged for preview with a fresh instance of
        /// wrapperPrefab.</summary>
        void SpawnCharacter(GameObject wrapperPrefab);
    }
}
