using System.Collections;
using CRPG.Navigation.Grid.Tilemaps;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace CRPG.Tests.PlayMode
{
    /// <summary>2D campaign stale-bake check: a location whose painted tiles changed since it was baked logs an error when it loads (the editor / development-build
    /// check), and a current one stays silent.</summary>
    public class BakeFingerprintPlayTests
    {
        private GameObject root;
        private SemanticTile tile;

        [TearDown]
        public void TearDown()
        {
            if (root != null) Object.Destroy(root);
            if (tile != null) Object.Destroy(tile);
        }

        private BakeFingerprint Build(bool paintAfterBake)
        {
            tile = ScriptableObject.CreateInstance<SemanticTile>();
            tile.Configure(TileSemantic.Ground, null, null);

            root = new GameObject("Location");
            root.SetActive(false); // BakeFingerprint.Start must not run until the scene is set up
            var gridGo = new GameObject("Grid");
            gridGo.transform.SetParent(root.transform, false);
            gridGo.AddComponent<Grid>().cellSwizzle = GridLayout.CellSwizzle.XZY;
            var mapGo = new GameObject("Ground");
            mapGo.transform.SetParent(gridGo.transform, false);
            var map = mapGo.AddComponent<Tilemap>();
            mapGo.AddComponent<TilemapRenderer>();
            map.orientation = Tilemap.Orientation.XZ;
            map.SetTile(new Vector3Int(0, 0, 0), tile);

            var baked = new GameObject("Baked");
            baked.transform.SetParent(root.transform, false);
            var fingerprint = baked.AddComponent<BakeFingerprint>();
            fingerprint.Record(gridGo.GetComponent<Grid>());

            if (paintAfterBake) map.SetTile(new Vector3Int(3, 3, 0), tile);
            return fingerprint;
        }

        [UnityTest]
        public IEnumerator ALocationPaintedAfterItsBakeLogsAnErrorNamingTheFixWhenItLoads()
        {
            Build(paintAfterBake: true);
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("changed since it was last baked.*Bake Selected Location"));
            root.SetActive(true);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ACurrentBakeStaysSilent()
        {
            var fingerprint = Build(paintAfterBake: false);
            root.SetActive(true);
            yield return null;

            Assert.IsTrue(fingerprint.IsCurrent(out var message), message);
            LogAssert.NoUnexpectedReceived();
        }
    }
}
