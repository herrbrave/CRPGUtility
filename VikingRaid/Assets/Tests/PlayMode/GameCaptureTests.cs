using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using VikingRaid.Game;

namespace VikingRaid.Tests
{
    /// <summary>
    /// Task 4 visual gate: images of the running game WITH its HUD. Screen capture does not work in a batch run (it never returns), so the game camera renders into one render texture
    /// and the UI Toolkit panel into another (PanelSettings.targetTexture); the two are composited here. Shows exploring, the stage walk, the fight's first frame, an arrow in flight,
    /// a hit, the victory banner and the Camp destroyed overlay. Written to VikingRaid_Logs/spikes/game_*.png; the only asserts are cheap (not black), a human judges the composition.
    /// </summary>
    public class GameCaptureTests : GameTestBase
    {
        private const int W = 1280, H = 720;
        private static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));
        private RenderTexture gameTexture, uiTexture;

        private void SetUpCapture()
        {
            gameTexture = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            uiTexture = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Cam.targetTexture = gameTexture;
            var settings = Object.FindFirstObjectByType<UIDocument>().panelSettings;
            settings.targetTexture = uiTexture;
            settings.clearColor = true; settings.colorClearValue = new Color(0f, 0f, 0f, 0f);
        }

        private static Texture2D Read(RenderTexture rt)
        {
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply(); RenderTexture.active = null;
            return tex;
        }

        private IEnumerator Shot(string name)
        {
            for (var i = 0; i < 3; i++) yield return null;     // let the camera and the panel render into their targets
            var game = Read(gameTexture); var ui = Read(uiTexture);
            var g = game.GetPixels32(); var u = ui.GetPixels32(); double luma = 0;
            for (var i = 0; i < g.Length; i++)
            {
                float a = u[i].a / 255f;                         // straight alpha over the game image
                g[i] = new Color32((byte)(u[i].r * a + g[i].r * (1f - a)), (byte)(u[i].g * a + g[i].g * (1f - a)), (byte)(u[i].b * a + g[i].b * (1f - a)), 255);
                luma += (0.2126 * g[i].r + 0.7152 * g[i].g + 0.0722 * g[i].b) / 255.0;
            }

            luma /= g.Length;
            game.SetPixels32(g); game.Apply();
            File.WriteAllBytes(Path.Combine(OutDir, $"game_{name}.png"), game.EncodeToPNG());
            Debug.Log($"[GameCapture] {name}: mean luminance {luma:F3}, ui alpha pixels {u.Length}");
            Object.DestroyImmediate(game); Object.DestroyImmediate(ui);
            Assert.Greater(luma, 0.03, $"{name} is black");
        }

        private IEnumerator Wait(float seconds) { var t = Time.realtimeSinceStartup; while (Time.realtimeSinceStartup - t < seconds) yield return null; }

        [TearDown]
        public void ReleaseCapture()
        {
            if (Cam != null) Cam.targetTexture = null;
            var doc = Object.FindFirstObjectByType<UIDocument>();
            if (doc != null && doc.panelSettings != null) doc.panelSettings.targetTexture = null;
            if (gameTexture != null) gameTexture.Release();
            if (uiTexture != null) uiTexture.Release();
        }

        [UnityTest]
        public IEnumerator CaptureTheGameLoop()
        {
            Directory.CreateDirectory(OutDir);
            SetUpCapture();
            yield return Wait(1.0f);
            yield return Shot("1_explore");

            Time.timeScale = 2f;
            yield return EnterTrigger(0);
            yield return Wait(0.8f);
            yield return Shot("2_staging");
            yield return WaitUntil(() => Game.State == GameState.Fighting, 40f, "the fight to start");
            Time.timeScale = 1f;
            yield return Wait(1.5f);
            yield return Shot("3_fight_start");

            // play on; catch an arrow in flight and a hit
            Time.timeScale = 2f;
            var party = new ScriptedParty(Game);
            var shotArrow = false; var shotBurst = false;
            var start = Time.realtimeSinceStartup;
            while (Game.State == GameState.Fighting && Time.realtimeSinceStartup - start < 120f)
            {
                party.Tick();
                if (!shotArrow && Game.Fx.ProjectilesSpawned > 0) { shotArrow = true; yield return Wait(0.12f); yield return Shot("4_arrow"); }
                else if (!shotBurst && Game.Fx.ImpactsSpawned > 0) { shotBurst = true; yield return Shot("5_hit"); }
                yield return null;
            }

            Time.timeScale = 1f;
            yield return Wait(0.8f);
            yield return Shot("6_victory");
            Debug.Log($"[GameCapture] camera pitch {Cam.transform.eulerAngles.x:F0} fov {Cam.fieldOfView:F0}");

            Game.OverlayElement.style.display = DisplayStyle.Flex;   // what the Camp destroyed screen looks like
            yield return Wait(0.3f);
            yield return Shot("7_camp_destroyed");
        }

        [UnityTest]
        public IEnumerator CaptureAnArrowInFlightUpClose()
        {
            Directory.CreateDirectory(OutDir);
            Time.timeScale = 3f;
            yield return EnterTrigger(0);
            yield return WaitUntil(() => Game.State == GameState.Fighting, 40f, "the fight to start");
            var sigrid = Game.Heroes[2];
            yield return WaitUntil(() =>
            {
                if (!Game.Combat.IsActive || Game.Combat.CurrentTurnId is not { } id || Game.Combat.Controller.Scheduler.IsBusy) return false;
                if (id == sigrid) return true;
                if (Game.Heroes.Contains(id)) Game.Combat.Controller.EncounterService.EndTurn();
                return false;
            }, 60f, "Sigrid's turn");
            var goblin = Game.GoblinsOf(0)[1].Id;
            var goblinSpot = Position(goblin);            // the shot can kill it (its agent leaves the live list at once), so remember where it stood
            VikingGame.FindAgent(sigrid).Motor.Warp(goblinSpot - WorldLayout.Arenas[0].Forward * 10f);
            yield return null;
            Game.Session.RulesDatabase.TryResolve<CRPG.Abilities.AbilityDefinition>(new CRPG.Core.DefinitionId("ability-vr-longbow"), out var bow);
            Game.Combat.Controller.Scheduler.StartAbility(sigrid, bow, new[] { goblin });

            GameObject arrow = null;
            var until = Time.realtimeSinceStartup + 10f;
            while (arrow == null && Time.realtimeSinceStartup < until) { arrow = GameObject.Find("SM_Wep_Arrow_01(Clone)"); yield return null; }
            Assert.IsNotNull(arrow, "no arrow was ever spawned");
            var rs = arrow.GetComponentsInChildren<Renderer>(true);
            Debug.Log($"[GameCapture] arrow found: pos {arrow.transform.position}, parent {(arrow.transform.parent == null ? "none" : arrow.transform.parent.name)}, scale {arrow.transform.lossyScale}, active {arrow.activeInHierarchy}, renderers {rs.Length}, enabled {(rs.Length > 0 && rs[0].enabled)}, sigrid at {Position(sigrid)}, goblin at {goblinSpot}, projectiles spawned {Game.Fx.ProjectilesSpawned}, rotation {arrow.transform.eulerAngles}");
            Time.timeScale = 0.3f;                        // slow, so the camera catches it mid-flight
            yield return null; yield return null;

            var rt = new RenderTexture(900, 560, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var camObject = new GameObject("ArrowCam"); var cam = camObject.AddComponent<Camera>();
            cam.fieldOfView = 40f; cam.targetTexture = rt; cam.clearFlags = CameraClearFlags.Skybox;
            var side = Vector3.Cross(Vector3.up, WorldLayout.Arenas[0].Forward);
            camObject.transform.position = arrow.transform.position + side * 3.2f + Vector3.up * 0.6f;
            camObject.transform.LookAt(arrow.transform.position);
            // wait until it is well clear of the archer, then shoot it from the side
            var clear = Time.realtimeSinceStartup + 10f;
            while (arrow != null && Vector3.Distance(arrow.transform.position, Position(sigrid)) < 4f && Time.realtimeSinceStartup < clear) yield return null;
            if (arrow == null) Assert.Fail("the arrow vanished before it could be photographed");
            camObject.transform.position = arrow.transform.position + side * 3.2f + Vector3.up * 0.6f;
            camObject.transform.LookAt(arrow.transform.position);
            for (var i = 0; i < 2; i++) yield return null;
            var tex = Read(rt);
            File.WriteAllBytes(Path.Combine(OutDir, "game_8_arrow_close.png"), tex.EncodeToPNG());
            Object.DestroyImmediate(tex); cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(camObject);
        }
    }
}
