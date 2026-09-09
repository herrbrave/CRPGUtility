namespace CRPG.Dialogue
{
    /// <summary>Who rolls a dialogue check (spec §46's "Active Character / Best Party Member / Specific Speaker /
    /// Entire Party"). SpecificSpeaker resolves to the conversation's NPC (spec's DialogueContext.Speaker --
    /// DialogueContext.NpcId here) rather than a party member; EntireParty has every current party member roll
    /// independently, succeeding if at least half do (5e's "group check" convention -- this is aggregation
    /// logic over rolls, not a 5e-specific rule, so it stays in the ruleset-agnostic engine).</summary>
    public enum CheckPerformer
    {
        ActiveCharacter,
        BestPartyMember,
        SpecificSpeaker,
        EntireParty,
    }
}
