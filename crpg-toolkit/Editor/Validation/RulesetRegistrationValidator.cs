using System;
using System.Collections.Generic;
using CRPG.Actors;
using CRPG.Core;
using CRPG.Gameplay;
using UnityEngine;

namespace CRPG.Editor.Validation
{
    /// <summary>
    /// Task 5.3: closes the "ruleset registrations" gap in Docs/Design/Production-Foundation.md sec.8's
    /// validator scope. Reuses the real, public registration path (<see cref="GameSessionHost.StartSession(IRulesetDefinition, IDiceService, IReadOnlyList{Type})"/>)
    /// rather than reimplementing duplicate-id/exception detection -- <c>RulesetSessionFactory</c> (the
    /// internal helper that actually does the work) is <c>internal</c> to <c>CRPG.Gameplay</c> and not visible
    /// here, but the public <c>StartSession</c> overload drives the identical logic.
    /// <para>
    /// Deliberately checks registration MECHANICS only -- does <c>Register()</c> complete without throwing --
    /// not content-completeness or game-specific required-policy completeness (<c>requiredPolicyTypes</c> is
    /// always null here, since a generic validator has no way to know what a specific game actually needs
    /// registered). A caller wanting "does this ruleset provide everything MY game needs" should pass its own
    /// required policy types to <c>StartSession</c> directly, not rely on this.
    /// </para>
    /// </summary>
    public static class RulesetRegistrationValidator
    {
        public static List<ValidationResult> Validate(IRulesetDefinition provider, string assetPath)
        {
            var results = new List<ValidationResult>();
            GameObject hostGo = null;
            GameSessionHost host = null;

            try
            {
                hostGo = new GameObject("RulesetRegistrationValidator_Temp");
                host = hostGo.AddComponent<GameSessionHost>();
                host.StartSession(provider, new DiceService(new SeededRandomSource(1)), requiredPolicyTypes: null);
            }
            catch (Exception ex)
            {
                results.Add(new ValidationResult(ValidationSeverity.Error, assetPath, "Register",
                    $"Registering this ruleset threw: {ex.Message}",
                    "Check Register() for a duplicate DefinitionId among the content being registered, a required policy that failed to construct, or a null reference."));
            }
            finally
            {
                // EndSession explicitly, rather than relying on GameSessionHost.OnDestroy -- DestroyImmediate's
                // OnDestroy callback does not reliably fire in headless/EditMode contexts (confirmed empirically
                // earlier in this project), and this validator has to work correctly there.
                host?.EndSession();
                if (hostGo != null) UnityEngine.Object.DestroyImmediate(hostGo);
            }

            return results;
        }
    }
}
