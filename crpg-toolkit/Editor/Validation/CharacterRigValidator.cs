using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Editor.Character;
using UnityEditor;
using UnityEngine;

namespace CRPG.Editor.Validation
{
    public enum ValidationSeverity
    {
        /// <summary>Blocks the relevant capability -- the wrapper can still be built, but the character will
        /// not behave correctly until this is fixed.</summary>
        Error,

        /// <summary>A recorded, non-blocking gap -- an optional clip with no authored fallback beyond
        /// "nothing plays," a rig family left unset (opt-in check, Task 2.4), etc.</summary>
        Warning,
    }

    /// <summary>One validation finding: what's wrong, where, and how to fix it -- Docs/Design/
    /// Production-Foundation.md sec.4.6's "Report asset path, field and actionable fix for every
    /// error."</summary>
    public sealed class ValidationResult
    {
        public ValidationResult(ValidationSeverity severity, string assetPath, string field, string message, string fix)
        {
            Severity = severity;
            AssetPath = assetPath;
            Field = field;
            Message = message;
            Fix = fix;
        }

        public ValidationSeverity Severity { get; }
        public string AssetPath { get; }
        public string Field { get; }
        public string Message { get; }
        public string Fix { get; }

        public override string ToString() => $"[{Severity}] {AssetPath} ({Field}): {Message} -- {Fix}";
    }

    /// <summary>
    /// Task 2.5 (Production Foundation): validates a <see cref="CharacterSetupSpec"/> against
    /// Docs/Design/Production-Foundation.md sec.4.6's rules -- pure logic, no GameObject/scene required, so it
    /// runs identically from CharacterSetupWindow's Validate button and from an automated test. Never mutates
    /// the spec or any asset ("No automatic 'fix all' changing design data," design sec.8).
    /// </summary>
    public static class CharacterRigValidator
    {
        public static List<ValidationResult> Validate(CharacterSetupSpec spec)
        {
            var results = new List<ValidationResult>();

            if (spec.Presentation == PresentationKind.Sprite)
            {
                // No model, clips, rig or sockets on a sprite card.
                ValidateSprite(spec, results);
            }
            else
            {
                ValidateModel(spec, results);
                ValidateAnimationProfile(spec, results);
                ValidateRigFamily(spec, results);
                ValidateSockets(spec, results);
            }

            if (spec.UsesTopDownCollider) ValidateTopDown(spec, results);
            else ValidateCapsule(spec, results);

            if (spec.Movement == MovementBackend.NavMesh) ValidateAgent(spec, results);
            else if (spec.GridSpeed <= 0f)
                results.Add(new ValidationResult(ValidationSeverity.Error, "(none)", nameof(spec.GridSpeed), "Grid speed must be positive.", "Set GridSpeed above 0 (world units per second)."));

            return results;
        }

        private static void ValidateTopDown(CharacterSetupSpec spec, List<ValidationResult> results)
        {
            if (spec.GridCellSize <= 0f)
                results.Add(new ValidationResult(ValidationSeverity.Error, "(none)", nameof(spec.GridCellSize), "Grid cell size must be positive.", "Set GridCellSize to the tile size in world units (1 for the 16px campaign)."));
            if (spec.GridColliderFill <= 0f || spec.GridColliderFill > 1f)
                results.Add(new ValidationResult(ValidationSeverity.Error, "(none)", nameof(spec.GridColliderFill), "Collider fill must be in (0, 1].", "Use about 0.7: a box of that fraction of the cell cannot reach the neighbouring cell when it rotates."));
            if (spec.GridColliderHeight <= 0f)
                results.Add(new ValidationResult(ValidationSeverity.Error, "(none)", nameof(spec.GridColliderHeight), "Collider height must be positive.", "Set GridColliderHeight above 0."));
        }

        private static void ValidateSprite(CharacterSetupSpec spec, List<ValidationResult> results)
        {
            var slots = spec.SpriteLayerSlots;
            if (slots == null || slots.Count == 0)
            {
                results.Add(new ValidationResult(ValidationSeverity.Error, "(none)", nameof(spec.SpriteLayerSlots), "No sprite layer slots.", "Keep the default set (SpriteLayerCompositor.DefaultSlots()) or list at least one slot."));
            }
            else
            {
                var seen = new HashSet<DefinitionId>();
                foreach (var slot in slots)
                {
                    if (!slot.IsValid) results.Add(new ValidationResult(ValidationSeverity.Error, "(none)", nameof(spec.SpriteLayerSlots), "A sprite layer slot has no id.", "Remove the empty entry."));
                    else if (!seen.Add(slot)) results.Add(new ValidationResult(ValidationSeverity.Error, "(none)", nameof(spec.SpriteLayerSlots), $"Duplicate sprite layer slot '{slot.Value}'.", "List each slot once."));
                }

                if (slots.Count > CRPG.Presentation.Sprites.SpriteSortingContract.MaxSlotsPerObject)
                    results.Add(new ValidationResult(ValidationSeverity.Error, "(none)", nameof(spec.SpriteLayerSlots),
                        $"{slots.Count} slots exceeds the maximum of {CRPG.Presentation.Sprites.SpriteSortingContract.MaxSlotsPerObject}.", "Remove slots; each character has a fixed band of draw orders."));
            }

            // A warning, not an error: the package ships no sorting layers, so a fresh project would fail hard; the compositor itself
            // falls back to Default with a warning.
            if (!SpriteCardBuilder.SortingLayerExists(spec.SpriteSortingLayer))
                results.Add(new ValidationResult(ValidationSeverity.Warning, "(none)", nameof(spec.SpriteSortingLayer),
                    $"Sorting layer '{spec.SpriteSortingLayer}' does not exist in this project.", "Add it under Project Settings > Tags and Layers > Sorting Layers, or the sprites draw on Default."));

            if (spec.SpriteCardHeight < 0f)
                results.Add(new ValidationResult(ValidationSeverity.Warning, "(none)", nameof(spec.SpriteCardHeight), "A negative card height puts the sprite below the floor.", "Use a small positive lift such as 0.02."));

            if (spec.ModelPrefab != null)
                results.Add(new ValidationResult(ValidationSeverity.Warning, AssetDatabase.GetAssetPath(spec.ModelPrefab), nameof(spec.ModelPrefab), "A Model prefab is ignored for a Sprite presentation (the card is generated).", "Clear it, or switch the presentation to Humanoid."));
        }

        private static void ValidateModel(CharacterSetupSpec spec, List<ValidationResult> results)
        {
            if (spec.ModelPrefab == null)
            {
                results.Add(new ValidationResult(ValidationSeverity.Error, "(none)", nameof(spec.ModelPrefab),
                    "No model prefab assigned.", "Assign a model prefab before building the wrapper."));
                return;
            }

            var assetPath = AssetDatabase.GetAssetPath(spec.ModelPrefab);
            var animator = spec.ModelPrefab.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, nameof(spec.ModelPrefab),
                    "Model has no Animator component.", "Add an Animator to the model (or a child) before it can drive any animation."));
                return;
            }

            var avatar = animator.avatar;
            if (avatar == null)
            {
                results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, nameof(spec.ModelPrefab),
                    "Model's Animator has no Avatar assigned.", "Configure the model's rig (Generic or Humanoid) in its Import Settings' Rig tab."));
            }
            else if (!avatar.isValid)
            {
                results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, nameof(spec.ModelPrefab),
                    "Model's Avatar is not valid.", "Re-check the Rig tab's bone mapping in the model's Import Settings."));
            }
            else if (avatar.isHuman)
            {
                // Corrected after a real, visually-reproduced bug (Phase 2 review follow-up -- see
                // Docs/Verification/Gate-A-Results.md): PlayableCharacterAnimationDriver plays clips via a
                // raw AnimationClipPlayable -> Animator output with NO Humanoid retargeting step at all (the
                // shipped content is Generic-imported, path-based curve data, not Humanoid muscle data).
                // Feeding that into a HUMANOID-avatar'd Animator doesn't just fail to retarget -- it actively
                // corrupts the pose: confirmed empirically with a live repro where a character collapsed to a
                // wrong, ground-level pose within about a second of Play starting, reproducible only through
                // Unity's own continuous graph evaluation (not a one-shot manual sample), and only briefly
                // masked by Animator.Rebind() before immediately re-corrupting. This system's characters must
                // use a Generic rig/avatar; Humanoid is for a DIFFERENT (AnimatorController/muscle-retargeting
                // based) pipeline this toolkit does not currently implement.
                results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, nameof(spec.ModelPrefab),
                    "Model uses a Humanoid avatar, which actively corrupts this Playables-based animation system.",
                    "Reconfigure the model's Import Settings -> Rig tab to Animation Type: Generic (not Humanoid), then re-import."));
            }
        }

        private static void ValidateAnimationProfile(CharacterSetupSpec spec, List<ValidationResult> results)
        {
            if (spec.AnimationProfile == null)
            {
                results.Add(new ValidationResult(ValidationSeverity.Error, "(none)", nameof(spec.AnimationProfile),
                    "No animation profile assigned.", "Assign a CharacterAnimationProfile -- baseline locomotion is blocked without one."));
                return;
            }

            var assetPath = AssetDatabase.GetAssetPath(spec.AnimationProfile);

            ValidateMandatoryCue(spec.AnimationProfile, spec.IdleCueId, nameof(spec.IdleCueId), assetPath, results);
            ValidateMandatoryCue(spec.AnimationProfile, spec.MovingCueId, nameof(spec.MovingCueId), assetPath, results);

            foreach (var optionalCueId in spec.OptionalCueIds)
            {
                if (!spec.AnimationProfile.TryGetCue(optionalCueId, out var entry))
                {
                    results.Add(new ValidationResult(ValidationSeverity.Warning, assetPath, nameof(spec.OptionalCueIds),
                        $"Optional cue '{optionalCueId}' is not authored in this profile.",
                        "Not blocking -- requests for this cue will simply have no visual (design sec.4.4's missing-clip handling). Author it if this capability matters for this character."));
                }
                else if (entry.Clip == null || entry.Clip.length <= 0f)
                {
                    results.Add(new ValidationResult(ValidationSeverity.Warning, assetPath, nameof(spec.OptionalCueIds),
                        $"Optional cue '{optionalCueId}' has no playable clip (null or zero-length).",
                        "Not blocking -- design sec.4.4 treats a missing/zero-length clip as an immediate no-op, not a soft-lock. Author a real clip if this capability matters for this character."));
                }
            }

            ValidateNoDuplicateCueIds(spec.AnimationProfile, assetPath, results);
        }

        // R8: TryGetCue only proves a matching id EXISTS -- it says nothing about whether that entry has a
        // real, playable clip. A profile whose idle/moving entries have a null or zero-length Clip previously
        // satisfied the mandatory-cue check and could build a wrapper that shows nothing at all for baseline
        // locomotion; that is exactly the "block the relevant capability" case design sec.4.6 asks for.
        private static void ValidateMandatoryCue(CharacterAnimationProfile profile, DefinitionId cueId, string fieldName, string assetPath, List<ValidationResult> results)
        {
            if (!profile.TryGetCue(cueId, out var entry))
            {
                results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, fieldName,
                    $"Mandatory cue '{cueId}' is not authored in this profile.",
                    $"Add a cue with id '{cueId}' to the profile, or change {fieldName} to match an existing one."));
                return;
            }

            if (entry.Clip == null)
            {
                results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, fieldName,
                    $"Mandatory cue '{cueId}' has no Clip assigned.",
                    $"Assign a real AnimationClip to the '{cueId}' entry -- a wrapper built from this profile would show nothing for baseline locomotion."));
            }
            else if (entry.Clip.length <= 0f)
            {
                results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, fieldName,
                    $"Mandatory cue '{cueId}''s Clip has zero length.",
                    $"Author a real, non-empty AnimationClip for '{cueId}'."));
            }
        }

        private static void ValidateNoDuplicateCueIds(CharacterAnimationProfile profile, string assetPath, List<ValidationResult> results)
        {
            var seen = new HashSet<DefinitionId>();
            foreach (var cue in profile.Cues)
            {
                if (!cue.CueId.IsValid) continue;
                if (!seen.Add(cue.CueId))
                {
                    results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, "Cues",
                        $"Duplicate cue id '{cue.CueId}' appears more than once in this profile.",
                        $"Remove or rename one of the '{cue.CueId}' entries -- TryGetCue silently resolves to only the first match, so a duplicate is dead, confusing content."));
                }
            }
        }

        private static void ValidateRigFamily(CharacterSetupSpec spec, List<ValidationResult> results)
        {
            if (spec.RigDefinition != null) return;

            // Opt-in, matching Task 2.4's EquipmentVisualBinder: DefinitionId.None on either side skips the
            // rig-family compatibility check entirely rather than rejecting real content that simply hasn't
            // authored rig families yet.
            results.Add(new ValidationResult(ValidationSeverity.Warning, "(none)", nameof(spec.RigDefinition),
                "No rig family declared.", "Skinned equipment's rig-family compatibility check (Task 2.4) will be skipped for this character. Assign a CharacterRigDefinition once one exists for this skeleton."));
        }

        private static void ValidateSockets(CharacterSetupSpec spec, List<ValidationResult> results)
        {
            if (spec.ModelPrefab == null || spec.RequiredSocketIds.Count == 0) return;

            var assetPath = AssetDatabase.GetAssetPath(spec.ModelPrefab);
            var namesInHierarchy = new HashSet<string>();
            foreach (var t in spec.ModelPrefab.GetComponentsInChildren<Transform>(true)) namesInHierarchy.Add(t.name);

            foreach (var socketId in spec.RequiredSocketIds)
            {
                if (namesInHierarchy.Contains(socketId.Value)) continue;

                results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, nameof(spec.RequiredSocketIds),
                    $"Required socket '{socketId}' has no matching Transform anywhere in the model.",
                    $"Add an empty child Transform named exactly '{socketId}' at the desired mount point, or remove this socket from RequiredSocketIds."));
            }
        }

        private static void ValidateCapsule(CharacterSetupSpec spec, List<ValidationResult> results)
        {
            if (spec.CapsuleRadius <= 0f)
            {
                results.Add(new ValidationResult(ValidationSeverity.Error, "(none)", nameof(spec.CapsuleRadius),
                    "Capsule radius must be positive.", "Set CapsuleRadius to a positive value (0.3 is a reasonable Humanoid default)."));
            }

            if (spec.CapsuleHeight < spec.CapsuleRadius * 2f)
            {
                results.Add(new ValidationResult(ValidationSeverity.Error, "(none)", nameof(spec.CapsuleHeight),
                    "Capsule height must be at least twice the radius (a capsule shorter than its own diameter is degenerate).",
                    "Increase CapsuleHeight or decrease CapsuleRadius."));
            }
        }

        private static void ValidateAgent(CharacterSetupSpec spec, List<ValidationResult> results)
        {
            if (spec.AgentRadius <= 0f)
                results.Add(new ValidationResult(ValidationSeverity.Error, "(none)", nameof(spec.AgentRadius), "Agent radius must be positive.", "Set AgentRadius to a positive value."));

            if (spec.AgentHeight <= 0f)
                results.Add(new ValidationResult(ValidationSeverity.Error, "(none)", nameof(spec.AgentHeight), "Agent height must be positive.", "Set AgentHeight to a positive value."));

            if (spec.AgentSpeed <= 0f)
                results.Add(new ValidationResult(ValidationSeverity.Error, "(none)", nameof(spec.AgentSpeed), "Agent speed must be positive.", "Set AgentSpeed to a positive value -- a zero-speed agent can never arrive anywhere."));
        }
    }
}
