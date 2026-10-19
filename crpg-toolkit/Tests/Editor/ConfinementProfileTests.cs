using System.Collections.Generic;
using System.Reflection;
using CRPG.Actors;
using CRPG.Core;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests
{
    public sealed class ConfinementProfileTests
    {
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
        private static AnimationCueClip Cue(string id, AnimationClip clip)
        {
            var cue = new AnimationCueClip(); Set(cue, "cueId", new DefinitionId(id)); Set(cue, "clip", clip); return cue;
        }
        [Test] public void RuntimeExtensionPreservesOriginalCuesAndStanceMappingsWithoutMutatingSource()
        {
            var source = ScriptableObject.CreateInstance<CharacterAnimationProfile>();
            var idle = new AnimationClip(); var move = new AnimationClip(); var hold = new AnimationClip();
            CharacterAnimationProfile clone = null;
            try
            {
                Set(source, "cues", new List<AnimationCueClip> { Cue("custom-idle", idle), Cue("custom-move", move) });
                var stance = new StanceLocomotionSet(); Set(stance, "stanceId", new DefinitionId("custom-stance"));
                Set(stance, "idleCueId", new DefinitionId("custom-idle")); Set(stance, "movingCueId", new DefinitionId("custom-move"));
                Set(source, "stanceLocomotionSets", new List<StanceLocomotionSet> { stance });
                clone = source.CloneWithAdditionalCue(Cue("confined", hold));
                Assert.AreNotSame(source, clone); Assert.AreEqual(2, source.Cues.Count); Assert.AreEqual(3, clone.Cues.Count);
                Assert.IsFalse(source.TryGetCue(new DefinitionId("confined"), out _));
                Assert.IsTrue(clone.TryGetCue(new DefinitionId("custom-idle"), out var entry)); Assert.AreSame(idle, entry.Clip);
                Assert.IsTrue(clone.TryGetStanceLocomotion(new DefinitionId("custom-stance"), out var idleId, out var moveId));
                Assert.AreEqual(new DefinitionId("custom-idle"), idleId); Assert.AreEqual(new DefinitionId("custom-move"), moveId);
            }
            finally { if (clone != null) Object.DestroyImmediate(clone); Object.DestroyImmediate(source); Object.DestroyImmediate(idle); Object.DestroyImmediate(move); Object.DestroyImmediate(hold); }
        }
        [Test] public void RuntimeExtensionRejectsMissingClipsAndAuthoredCueReplacement()
        {
            var source = ScriptableObject.CreateInstance<CharacterAnimationProfile>(); var clip = new AnimationClip();
            try
            {
                Set(source, "cues", new List<AnimationCueClip> { Cue("existing", clip) });
                Assert.Throws<System.ArgumentException>(() => source.CloneWithAdditionalCue(Cue("confined", null)));
                Assert.Throws<System.InvalidOperationException>(() => source.CloneWithAdditionalCue(Cue("existing", clip)));
                Assert.AreEqual(1, source.Cues.Count);
            }
            finally { Object.DestroyImmediate(source); Object.DestroyImmediate(clip); }
        }
    }
}
