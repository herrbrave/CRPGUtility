using System.Linq;
using CRPG.Core;
using CRPG.Editor.Validation;
using NUnit.Framework;

namespace CRPG.Tests.EditMode
{
    /// <summary>Task 3.4 (Production Foundation): DialogueValidator's checks, exercised directly against
    /// synthetic DialogueDefinitions built via DialogueTestFactory -- no scene, matching the class's own
    /// "pure logic" design (same rationale as CharacterRigValidatorTests).</summary>
    public class DialogueValidatorTests
    {
        [Test]
        public void AWellFormedDialogueProducesNoResults()
        {
            var end = DialogueTestFactory.CreateEnd("end");
            var line = DialogueTestFactory.CreateLine("start", "Grimjaw", "Halt.", "end");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", line, end);

            var results = DialogueValidator.Validate(dialogue);

            Assert.IsEmpty(results);
        }

        [Test]
        public void AMissingStartNodeIsAnError()
        {
            var end = DialogueTestFactory.CreateEnd("end");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "nonexistent-start", end);

            var results = DialogueValidator.Validate(dialogue);

            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Error && r.Field == "StartNodeId"));
        }

        [Test]
        public void ADuplicateNodeIdIsAnError()
        {
            var endA = DialogueTestFactory.CreateEnd("end");
            var endB = DialogueTestFactory.CreateEnd("end"); // same seed -> same SerializableGuid
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "end", endA, endB);

            var results = DialogueValidator.Validate(dialogue);

            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("More than one node")));
        }

        [Test]
        public void ALineNodeDanglingReferenceIsAnError()
        {
            var line = DialogueTestFactory.CreateLine("start", "Grimjaw", "Halt.", "nonexistent-node");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", line);

            var results = DialogueValidator.Validate(dialogue);

            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Error && r.Field.Contains("LineNode") && r.Field.Contains("NextNodeId")));
        }

        [Test]
        public void AChoiceOptionDanglingReferenceIsAnError()
        {
            var choice = DialogueTestFactory.CreateChoice("start", DialogueTestFactory.CreateOption("Leave", "nonexistent-node"));
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", choice);

            var results = DialogueValidator.Validate(dialogue);

            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Error && r.Field.Contains("Options[0]")));
        }

        [Test]
        public void ABranchNodeDanglingReferenceOnEitherSideIsAnError()
        {
            var branch = DialogueTestFactory.CreateBranch("start", "flag", TimelineConditionType.HasOccurred, 0, "nonexistent-true", "nonexistent-false");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", branch);

            var results = DialogueValidator.Validate(dialogue);

            Assert.AreEqual(2, results.Count(r => r.Severity == ValidationSeverity.Error && r.Field.Contains("BranchNode")));
        }

        [Test]
        public void ANodeUnreachableFromStartIsAWarningNotAnError()
        {
            var end = DialogueTestFactory.CreateEnd("end");
            var line = DialogueTestFactory.CreateLine("start", "Grimjaw", "Halt.", "end");
            var orphan = DialogueTestFactory.CreateEnd("orphan"); // authored but nothing points to it
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", line, end, orphan);

            var results = DialogueValidator.Validate(dialogue);

            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Warning && r.Message.Contains(orphan.NodeId.ToString())));
            Assert.IsFalse(results.Any(r => r.Severity == ValidationSeverity.Error));
        }

        [Test]
        public void AnInescapableActionCycleIsAnError()
        {
            var recordA = DialogueTestFactory.CreateRecordEventAction("a-ran");
            var recordB = DialogueTestFactory.CreateRecordEventAction("b-ran");
            var nodeA = DialogueTestFactory.CreateAction("a", recordA, "b");
            var nodeB = DialogueTestFactory.CreateAction("b", recordB, "a");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "a", nodeA, nodeB);

            var results = DialogueValidator.Validate(dialogue);

            Assert.AreEqual(2, results.Count(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("can never reach a Line/Choice/Check/End")));
        }

        [Test]
        public void ABranchNodeWithOnlyOneEscapingSideIsStillAnError()
        {
            // TrueNodeId escapes to a real End; FalseNodeId loops back to the branch itself -- since a real
            // Timeline state could deterministically always take the False path, this must still be flagged.
            var end = DialogueTestFactory.CreateEnd("end");
            var branch = DialogueTestFactory.CreateBranch("start", "flag", TimelineConditionType.HasOccurred, 0, "end", "start");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", branch, end);

            var results = DialogueValidator.Validate(dialogue);

            Assert.IsTrue(results.Any(r => r.Severity == ValidationSeverity.Error && r.Message.Contains("can never reach a Line/Choice/Check/End")));
        }

        [Test]
        public void ABranchNodeWithBothSidesEscapingIsNotFlagged()
        {
            var trueEnd = DialogueTestFactory.CreateEnd("true-end");
            var falseEnd = DialogueTestFactory.CreateEnd("false-end");
            var branch = DialogueTestFactory.CreateBranch("start", "flag", TimelineConditionType.HasOccurred, 0, "true-end", "false-end");
            var dialogue = DialogueTestFactory.CreateDialogue("dlg-test", "start", branch, trueEnd, falseEnd);

            var results = DialogueValidator.Validate(dialogue);

            Assert.IsEmpty(results);
        }
    }
}
