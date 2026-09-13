using System.Collections.Generic;
using CRPG.Core;

namespace CRPG.Actors
{
    /// <summary>
    /// The typed registration surface an <see cref="IRulesetDefinition"/> writes into during session
    /// construction (Docs/Design/Production-Foundation.md sec.3's "ruleset entrypoint receives a typed
    /// registration builder"). Exposes just enough of the in-progress session -- the content database and the
    /// character registry -- for a provider to construct real policy instances (e.g. a skill-check rule needs
    /// <see cref="Characters"/> to read stats/proficiencies) without the provider needing a reference to
    /// <c>CRPG.Gameplay</c>'s <c>GameSession</c> itself.
    /// </summary>
    public interface IRulesetRegistrationBuilder
    {
        IRulesDatabase RulesDatabase { get; }
        ICharacterRegistry Characters { get; }
        IDiceService DiceService { get; }

        void RegisterDefinitions(IEnumerable<IDefinition> definitions);

        /// <summary>Registers a concrete policy instance under its interface type (e.g.
        /// <c>RegisterPolicy&lt;IArmorClassRule&gt;(new DefaultArmorClassRule(...))</c>). Throws if a policy for
        /// <typeparamref name="TPolicy"/> is already registered -- see <see cref="CRPG.Rules.PolicyRegistry"/>.</summary>
        void RegisterPolicy<TPolicy>(TPolicy policy) where TPolicy : class;
    }
}
