using System;

namespace CRPG.Dialogue
{
    /// <summary>A silent side effect an ActionNode performs -- covers spec's "Set Flag"/"Give Item"/"Start
    /// Quest" node kinds as one polymorphic, [SerializeReference]-authored family, the same pattern
    /// AbilityEffect uses. Only RecordEvent and GiveItem are implemented; a StartQuest kind can be added the
    /// same way once Quests exists, and an OpenStore kind once Store is built.</summary>
    [Serializable]
    public abstract class DialogueAction
    {
        public abstract void Execute(DialogueContext context);
    }
}
