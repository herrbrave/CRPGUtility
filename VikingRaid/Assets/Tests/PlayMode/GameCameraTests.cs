using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VikingRaid.Game;

namespace VikingRaid.Tests
{
    /// <summary>The player can move the camera: pan detaches it from the party until Recenter, rotating changes the view, a fight reframes it, and it follows the SELECTED group's centre.</summary>
    public class GameCameraTests : GameTestBase
    {
        private VikingCamera Rig => Game.CameraRig;

        private IEnumerator Frames(int count) { for (var i = 0; i < count; i++) yield return null; }

        private Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        [UnityTest]
        public IEnumerator PanningDetachesTheCameraFromThePartyAndRecenterBringsItBack()
        {
            yield return Frames(30);
            var start = Flat(Rig.Focus);
            Assert.Less(Vector3.Distance(start, Flat(PartyCentre())), 2.5f, "the camera starts on the party");

            var panning = true;
            Rig.InputOverride = () => new CameraInput { Pan = panning ? Vector2.up : Vector2.zero };
            var until = Time.realtimeSinceStartup + 1.0f;
            while (Time.realtimeSinceStartup < until) yield return null;
            panning = false;
            yield return Frames(40);
            var panned = Flat(Rig.Focus);
            Assert.Greater(Vector3.Distance(panned, start), 8f, "W moved the camera");
            Assert.IsTrue(Rig.Controller.ManualOverride);

            // the party walks off; the camera stays where the player put it
            Time.timeScale = 4f;
            Click(WorldLayout.PointAt(WorldLayout.Arenas[0].WestS - 40f));
            yield return WaitUntil(() => Vector3.Distance(Flat(PartyCentre()), WorldLayout.PointAt(WorldLayout.Arenas[0].WestS - 40f)) < 5f, 40f, "the party to walk");
            Time.timeScale = 1f;
            yield return Frames(20);
            Assert.Less(Vector3.Distance(Flat(Rig.Focus), panned), 3f, "a panned camera must not follow the party");

            var recenter = true;
            Rig.InputOverride = () => { var r = new CameraInput { Recenter = recenter }; recenter = false; return r; };
            var settle = Time.realtimeSinceStartup + 3f;
            while (Time.realtimeSinceStartup < settle && Vector3.Distance(Flat(Rig.Focus), Flat(PartyCentre())) > 2.5f) yield return null;
            Assert.IsFalse(Rig.Controller.ManualOverride);
            Assert.Less(Vector3.Distance(Flat(Rig.Focus), Flat(PartyCentre())), 2.5f, "Space brings the camera back to the party");
        }

        [UnityTest]
        public IEnumerator RotatingChangesWhereTheCameraLooksAndAFightReframesTheArena()
        {
            yield return Frames(30);
            var yawBefore = Rig.transform.eulerAngles.y;
            var rotating = true;
            Rig.InputOverride = () => new CameraInput { Rotate = rotating ? 1f : 0f, Pan = rotating ? Vector2.up : Vector2.zero };
            var until = Time.realtimeSinceStartup + 0.8f;
            while (Time.realtimeSinceStartup < until) yield return null;
            rotating = false;
            yield return Frames(60);
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(yawBefore, Rig.transform.eulerAngles.y)), 20f, "Q/E turn the view");
            Assert.IsTrue(Rig.Controller.ManualOverride);

            // a fight starting clears the player's camera and frames the action
            Time.timeScale = 3f;
            yield return EnterTrigger(0);
            yield return WaitUntil(() => Game.State == GameState.Fighting, 40f, "the fight to start");
            Time.timeScale = 1f;
            Assert.IsFalse(Rig.Controller.ManualOverride, "the fight reframes the camera");
            Assert.AreEqual(0f, Rig.Controller.YawOffset, 1e-3f);
            Assert.IsTrue(Rig.Tactical);
            var arenaFocus = Flat(Game.Anchors.encounters[0].cameraTarget.position);
            var settle = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < settle && Vector3.Distance(Flat(Rig.Focus), arenaFocus) > 2.5f) yield return null;
            Assert.Less(Vector3.Distance(Flat(Rig.Focus), arenaFocus), 2.5f, "the camera looks at the middle of the action");
        }

        [UnityTest]
        public IEnumerator TheCameraFollowsTheCentreOfTheSelectedHeroesNotOneOfThem()
        {
            Rig.InputOverride = () => default;
            Game.Session.Party.Select(Game.Heroes[0]);                   // only Bjorn
            var bjorn = Flat(Position(Game.Heroes[0]));
            var settle = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < settle && Vector3.Distance(Flat(Rig.Focus), bjorn) > 1.5f) yield return null;
            Assert.Less(Vector3.Distance(Flat(Rig.Focus), bjorn), 1.5f, "with one hero selected the camera follows him");

            Game.Session.Party.SelectAll();
            var centre = Flat(PartyCentre());
            settle = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < settle && Vector3.Distance(Flat(Rig.Focus), centre) > 1.5f) yield return null;
            Assert.Less(Vector3.Distance(Flat(Rig.Focus), centre), 1.5f, "with everyone selected it follows the party's centre");
        }

        [UnityTest]
        public IEnumerator TheCameraCanBePannedAndRotatedDuringAFightAndSpaceReframesIt()
        {
            Time.timeScale = 3f;
            yield return EnterTrigger(0);
            yield return WaitUntil(() => Game.State == GameState.Fighting, 40f, "the fight to start");
            Time.timeScale = 1f;
            var arenaFocus = Flat(Game.Anchors.encounters[0].cameraTarget.position);
            var settle = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < settle && Vector3.Distance(Flat(Rig.Focus), arenaFocus) > 2.5f) yield return null;
            var yawBefore = Rig.transform.eulerAngles.y;

            // look around mid-fight: pan forward and turn the view while the fight is still going
            var input = new CameraInput { Pan = Vector2.up, Rotate = 1f };
            Rig.InputOverride = () => input;
            var until = Time.realtimeSinceStartup + 0.9f;
            while (Time.realtimeSinceStartup < until) yield return null;
            input = default;
            var ease = Time.realtimeSinceStartup + 1.5f;       // let the camera finish easing to where the player put it (real time: batch frames are very fast)
            while (Time.realtimeSinceStartup < ease) yield return null;

            Assert.AreEqual(GameState.Fighting, Game.State, "the fight is still going while the player looks around");
            Assert.IsTrue(Rig.Controller.ManualOverride, "panning mid-fight must stick: the fight must not snatch the camera back");
            Assert.Greater(Vector3.Distance(Flat(Rig.Focus), arenaFocus), 8f, "W moved the camera away from the arena centre");
            Assert.Greater(Mathf.Abs(Mathf.DeltaAngle(yawBefore, Rig.transform.eulerAngles.y)), 15f, "Q/E turned the view mid-fight");
            var held = Flat(Rig.Focus);
            var hold = Time.realtimeSinceStartup + 1.5f;
            while (Time.realtimeSinceStartup < hold) yield return null;
            Assert.Less(Vector3.Distance(Flat(Rig.Focus), held), 1f, "it stays where the player put it for as long as the fight runs");

            // Space puts the whole fight back in frame
            var recenter = true;
            Rig.InputOverride = () => { var r = new CameraInput { Recenter = recenter }; recenter = false; return r; };
            settle = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < settle && Vector3.Distance(Flat(Rig.Focus), arenaFocus) > 2.5f) yield return null;
            Assert.IsFalse(Rig.Controller.ManualOverride);
            Assert.Less(Vector3.Distance(Flat(Rig.Focus), arenaFocus), 2.5f, "Space reframes the fight");
        }
    }
}
