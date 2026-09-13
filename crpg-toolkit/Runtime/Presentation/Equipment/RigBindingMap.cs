using System.Collections.Generic;
using CRPG.Core;
using UnityEngine;

namespace CRPG.Presentation.Equipment
{
    /// <summary>
    /// Task 2.4 (Production Foundation): a reusable, explicit source-&gt;target bone mapping for one rig
    /// family -- Docs/Design/Production-Foundation.md sec.4.5's "explicit source-&gt;target relative bone
    /// paths or validated mapping asset." Replaces the old bone-name guesswork (matching a garment's bones
    /// onto a character's skeleton purely by leaf Transform.name, silently leaving an unmatched bone pointing
    /// at the garment's own unused copy) with authored, validated PATHS -- so two differently-placed bones
    /// that happen to share a leaf name (a real, not hypothetical, risk once more than one rig family is in
    /// play) can never be confused for each other. Shared/reusable across every garment authored against the
    /// same rig family, rather than re-typed per item.
    /// </summary>
    [CreateAssetMenu(menuName = "CRPG Toolkit/Presentation/Rig Binding Map")]
    public sealed class RigBindingMap : DefinitionAsset
    {
        /// <summary>One bone mapping: <see cref="SourcePath"/> is relative to the garment prefab's own root;
        /// <see cref="TargetPath"/> is relative to the character's CharacterRoot (matching how
        /// EquipmentVisualBinder builds its own path lookup).</summary>
        [System.Serializable]
        public sealed class BonePathEntry
        {
            [SerializeField] private string sourcePath;
            [SerializeField] private string targetPath;

            public string SourcePath => sourcePath;
            public string TargetPath => targetPath;
        }

        [SerializeField] private DefinitionId rigFamilyId;
        [SerializeField] private string rootBonePath;
        [SerializeField] private List<BonePathEntry> bonePaths = new();

        /// <summary>Which <see cref="CharacterRigDefinition"/> this map's target paths are authored against --
        /// EquipmentVisualBinder rejects binding a garment using this map onto a character declaring a
        /// different rig family, rather than silently producing a half-bound mesh.</summary>
        public DefinitionId RigFamilyId => rigFamilyId;

        /// <summary>The garment's own root bone path (relative to its prefab root) -- used as a
        /// SkinnedMeshRenderer.rootBone fallback when a renderer's authored rootBone isn't itself one of
        /// <see cref="BonePaths"/>'s explicitly mapped source bones.</summary>
        public string RootBonePath => rootBonePath;

        public IReadOnlyList<BonePathEntry> BonePaths => bonePaths;

        /// <summary>Structural validation only (empty/duplicate paths) -- doesn't need a live skeleton to run,
        /// so it can (and should) run before any renderer is touched. Missing-bone validation against a real
        /// character/garment hierarchy happens separately in EquipmentVisualBinder, the only place live
        /// Transforms actually exist to check against.</summary>
        public bool Validate(out string error)
        {
            var seenSource = new HashSet<string>();
            var seenTarget = new HashSet<string>();

            foreach (var entry in bonePaths)
            {
                if (string.IsNullOrEmpty(entry.SourcePath) || string.IsNullOrEmpty(entry.TargetPath))
                {
                    error = "A bone path entry has an empty source or target path.";
                    return false;
                }

                if (!seenSource.Add(entry.SourcePath))
                {
                    error = $"Duplicate source bone path '{entry.SourcePath}'.";
                    return false;
                }

                if (!seenTarget.Add(entry.TargetPath))
                {
                    error = $"Duplicate target bone path '{entry.TargetPath}'.";
                    return false;
                }
            }

            error = null;
            return true;
        }
    }
}
