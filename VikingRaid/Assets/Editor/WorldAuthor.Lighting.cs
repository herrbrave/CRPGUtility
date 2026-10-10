using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VikingRaid.Game;

namespace VikingRaid.EditorTools
{
    /// <summary>
    /// Stage D: dusk. The Synty "skybox" materials are mesh materials (an unlit dome shader), not skyboxes, so the sky is the built-in procedural one: it supplies the horizon the
    /// fog matches AND the environment the reflection probes capture (the Task 1 weapon metals went near-black with nothing to reflect). Three baked probes cover the road; fire
    /// light is the point lights authored beside each flame (see the camp stage). Needs a real graphics device: run WITHOUT -nographics.
    /// </summary>
    public static partial class WorldAuthor
    {
        private static readonly Color FogColor = new(0.50f, 0.56f, 0.64f);

        private static void BuildLighting()
        {
            var sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetColor("_SkyTint", new Color(0.46f, 0.52f, 0.66f));
            sky.SetColor("_GroundColor", new Color(0.20f, 0.22f, 0.22f));
            sky.SetFloat("_Exposure", 1.0f); sky.SetFloat("_AtmosphereThickness", 1.15f); sky.SetFloat("_SunSize", 0.05f); sky.SetFloat("_SunSizeConvergence", 5f);
            AssetDatabase.CreateAsset(sky, $"{MaterialsDir}/DuskSky.mat");
            RenderSettings.skybox = sky;

            var sunObject = new GameObject("Sun"); sunObject.transform.SetParent(lightRoot, false);
            sunObject.transform.rotation = Quaternion.Euler(28f, 62f, 0f);   // 28 degrees above the horizon (18 made 40 m tree shadows that streaked across every arena), travelling along the road: the party's back is to the dusk sun
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional; sun.color = new Color(1f, 0.72f, 0.5f); sun.intensity = 1.5f;
            sun.shadows = LightShadows.Soft; sun.shadowStrength = 0.9f; sun.shadowBias = 0.05f; sun.shadowNormalBias = 0.4f;
            RenderSettings.sun = sun;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.43f, 0.50f, 0.63f);
            RenderSettings.ambientEquatorColor = new Color(0.33f, 0.35f, 0.35f);
            RenderSettings.ambientGroundColor = new Color(0.17f, 0.16f, 0.14f);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = 0.010f; RenderSettings.fogColor = FogColor;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox; RenderSettings.reflectionIntensity = 1f;

            BuildPostProcessing();
            BuildAmbientFx();
            BuildProbes();
        }

        private static void BuildPostProcessing()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(); bloom.threshold.Override(1.0f); bloom.intensity.Override(0.25f); bloom.scatter.Override(0.6f);
            var tone = profile.Add<Tonemapping>(); tone.mode.Override(TonemappingMode.ACES);
            var colour = profile.Add<ColorAdjustments>(); colour.contrast.Override(8f); colour.saturation.Override(6f);
            var vignette = profile.Add<Vignette>(); vignette.intensity.Override(0.22f); vignette.smoothness.Override(0.5f);
            AssetDatabase.CreateAsset(profile, $"{GeneratedDir}/PostProcessing.asset");
            AssetDatabase.AddObjectToAsset(bloom, profile); AssetDatabase.AddObjectToAsset(tone, profile); AssetDatabase.AddObjectToAsset(colour, profile); AssetDatabase.AddObjectToAsset(vignette, profile);

            var volumeObject = new GameObject("PostProcessing"); volumeObject.transform.SetParent(lightRoot, false);
            var volume = volumeObject.AddComponent<Volume>(); volume.isGlobal = true; volume.sharedProfile = profile; volume.priority = 1f;
        }

        private static void BuildAmbientFx()
        {
            var rng = new System.Random(Seed + 2);
            float Rf(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            void Fx(string path, Vector3 position, string name)
            {
                var fx = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), fxRoot);
                fx.transform.position = position; fx.name = name;
            }

            // (ground mist and dust FX were tried and cut: the Synty fog prefabs render as flat grey polyhedra; the exponential fog does the atmosphere)

            // fireflies and drifting leaves along the first two woods stretches; dust blowing across the forecourt
            for (var s = 18f; s < WorldLayout.Arenas[1].WestS; s += 24f)
            {
                var p = WorldLayout.PointAt(s); var t = WorldLayout.TangentAt(s); var right = new Vector3(t.z, 0, -t.x);
                Fx(WorldAssets.Fireflies, p + right * Rf(-1f, 1f) * (WorldLayout.HalfWidthAt(s) + 3f) + Vector3.up * 1.2f, "Fireflies");
                Fx(WorldAssets.Leaves, p + right * Rf(-1f, 1f) * 4f + Vector3.up * 5f, "Leaves");
            }

        }

        private static void BuildProbes()
        {
            // three baked probes along the road; every static object is flagged ReflectionProbeStatic, so the probes see the trees, the camp and the sky
            WorldGeometry.Bounds(out var min, out var max);
            var stations = new[] { 70f, 200f, WorldLayout.RoadLength - 55f };
            for (var i = 0; i < stations.Length; i++)
            {
                var go = new GameObject($"ReflectionProbe_{i}"); go.transform.SetParent(lightRoot, false);
                go.transform.position = WorldLayout.PointAt(stations[i]) + Vector3.up * 2f;     // the height of a standing hero's chest, not the canopy
                var probe = go.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Baked; probe.resolution = 128; probe.boxProjection = false; probe.importance = 1; probe.blendDistance = 25f;
                probe.size = new Vector3(190f, 40f, 170f); probe.center = Vector3.zero; probe.hdr = true; probe.intensity = 1f; probe.farClipPlane = 220f;
                probe.cullingMask = ~0;
                var path = $"{GeneratedDir}/ReflectionProbe_{i}.exr";
                if (!Lightmapping.BakeReflectionProbe(probe, path)) throw new System.InvalidOperationException($"baking reflection probe {i} failed (is a graphics device available? do not run with -nographics)");
            }

            // BakeReflectionProbe writes the file but does not reliably hand the imported cubemap back to the probe: assign it explicitly (a probe with no texture reflects nothing)
            AssetDatabase.Refresh();
            foreach (var probe in lightRoot.GetComponentsInChildren<ReflectionProbe>())
            {
                var index = probe.name.Substring(probe.name.LastIndexOf('_') + 1);
                var path = $"{GeneratedDir}/ReflectionProbe_{index}.exr";
                AssetDatabase.ImportAsset(path);
                var cubemap = AssetDatabase.LoadAssetAtPath<Texture>(path);
                if (cubemap == null) throw new System.InvalidOperationException($"{path} did not import as a texture");
                probe.mode = ReflectionProbeMode.Custom;   // a Baked-mode probe keeps its texture in scene lighting data, which this scene does not have; Custom stores the reference on the probe itself
                probe.customBakedTexture = cubemap;
                EditorUtility.SetDirty(probe);
            }
        }
    }
}
