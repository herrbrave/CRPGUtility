using System.Collections.Generic;
using CRPG.Camera;
using CRPG.Core;
using CRPG.Exploration;
using CRPG.Gameplay;
using CRPG.Party;
using NUnit.Framework;
using UnityEngine;

namespace CRPG.Tests.EditMode
{
    public class PartyCameraFollowTests
    {
        private sealed class FakeSelectableSource : ISelectableSource
        {
            private readonly List<SelectableEntity> entities = new();
            public void Add(RuntimeEntityId id, Vector3 position) => entities.Add(new SelectableEntity(id, position));
            public IEnumerable<SelectableEntity> GetSelectables() => entities;
        }

        private static CameraRigState CreateRig() => new(ScriptableObject.CreateInstance<CRPGCameraSettings>());

        [Test]
        public void TickFollowsTheActiveCharactersCurrentPosition()
        {
            var character = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { character });
            var selectables = new FakeSelectableSource();
            selectables.Add(character, new Vector3(5f, 0f, 5f));
            var rig = CreateRig();
            rig.SnapTo(Vector3.zero);
            var follow = new PartyCameraFollow(party, selectables, rig);

            follow.Tick(deltaTime: 0.1f);

            Assert.Greater(rig.FocusPoint.x, 0f); // moved toward the active character, not left at the origin
        }

        [Test]
        public void SnapToActiveCharacterLandsExactlyInOneCall()
        {
            var character = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { character });
            var selectables = new FakeSelectableSource();
            var position = new Vector3(12f, 0f, -4f);
            selectables.Add(character, position);
            var rig = CreateRig();
            var follow = new PartyCameraFollow(party, selectables, rig);

            follow.SnapToActiveCharacter();

            Assert.AreEqual(position, rig.FocusPoint);
        }

        [Test]
        public void TickNoOpsWhenThereIsNoActiveCharacter()
        {
            var party = new PartyState(); // no members, no active character
            var selectables = new FakeSelectableSource();
            var rig = CreateRig();
            rig.SnapTo(new Vector3(1f, 2f, 3f));
            var follow = new PartyCameraFollow(party, selectables, rig);

            Assert.DoesNotThrow(() => follow.Tick(0.1f));
            Assert.AreEqual(new Vector3(1f, 2f, 3f), rig.FocusPoint);
        }

        [Test]
        public void TickNoOpsWhenTheActiveCharacterIsMissingFromTheSelectableSource()
        {
            var character = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { character });
            var selectables = new FakeSelectableSource(); // deliberately never Add()'d -- destroyed/unspawned
            var rig = CreateRig();
            rig.SnapTo(new Vector3(1f, 2f, 3f));
            var follow = new PartyCameraFollow(party, selectables, rig);

            Assert.DoesNotThrow(() => follow.Tick(0.1f));
            Assert.AreEqual(new Vector3(1f, 2f, 3f), rig.FocusPoint);
        }

        [Test]
        public void TickRetargetsWhenTheActiveCharacterChanges()
        {
            var first = RuntimeEntityId.NewId();
            var second = RuntimeEntityId.NewId();
            var party = new PartyState(new[] { first, second });
            party.Select(first);
            var selectables = new FakeSelectableSource();
            selectables.Add(first, new Vector3(10f, 0f, 0f));
            selectables.Add(second, new Vector3(-10f, 0f, 0f));
            var rig = CreateRig();
            rig.SnapTo(Vector3.zero);
            var follow = new PartyCameraFollow(party, selectables, rig);

            follow.Tick(0.1f);
            Assert.Greater(rig.FocusPoint.x, 0f);

            party.Select(second);
            rig.SnapTo(Vector3.zero);
            follow.Tick(0.1f);
            Assert.Less(rig.FocusPoint.x, 0f);
        }
    }
}
