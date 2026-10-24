using System;
using System.Collections.Generic;
using System.IO;
using CRPG.Maps;
using UnityEditor;
using UnityEngine;
using UCamera = UnityEngine.Camera;

namespace CRPG.Editor
{
    /// <summary>What to render: the exact world rectangle the image will cover, and how.</summary>
    public sealed class MapBakeOptions
    {
        /// <summary>The world rectangle (Rect.x = world X, Rect.y = world Z) the image covers. It is the rectangle written into the map definition, so the image and the markers cannot disagree.</summary>
        public Rect WorldBoundsXZ;

        /// <summary>Pixels along the longer side; the other side follows the rectangle's aspect.</summary>
        public int LongSidePixels = 2048;

        public int CullingMask = ~0;
        public Color Background = new Color(0.10f, 0.11f, 0.13f, 1f);

        /// <summary>How high above the origin the camera stands (it must be above the tallest thing to show) and how far it can see down.</summary>
        public float CameraHeight = 400f;
        public float FarClip = 1000f;

        /// <summary>Called with the temporary camera before it renders: hide a layer, switch a roof off, set a replacement shader. Optional.</summary>
        public Action<UCamera> ConfigureCamera;

        public int AntiAliasing = 4;
    }

    /// <summary>
    /// The editor baker for location maps: a top-down orthographic render of the open scene through the project's render pipeline, framed to an exact world rectangle, north up (world +Z at the top of the image, +X to the right), so a map definition's bounds are the camera's
    /// frustum and markers land where things are. Generic: it knows nothing of any game; the host opens the scene, chooses the rectangle and the layers. <see cref="Stylise"/> (see <see cref="MapStylise"/>) is a separate, pure pass.
    /// </summary>
    public static class LocationMapBaker
    {
        /// <summary>The image size for a rectangle: the long side as asked, the other rounded to the rectangle's aspect. Throws when rounding cannot honour the aspect to the map definition's tolerance (a sliver of a rectangle).</summary>
        public static Vector2Int PixelSize(Rect bounds, int longSidePixels)
        {
            if (!(bounds.width > 0f) || !(bounds.height > 0f)) throw new ArgumentException("The map rectangle needs a positive width and height.", nameof(bounds));
            if (longSidePixels < 16) throw new ArgumentOutOfRangeException(nameof(longSidePixels));
            var wide = bounds.width >= bounds.height;
            var shortSide = Mathf.Max(1, Mathf.RoundToInt(longSidePixels * (wide ? bounds.height / bounds.width : bounds.width / bounds.height)));
            var size = wide ? new Vector2Int(longSidePixels, shortSide) : new Vector2Int(shortSide, longSidePixels);
            var imageAspect = (float)size.x / size.y; var worldAspect = bounds.width / bounds.height;
            if (Mathf.Abs(imageAspect - worldAspect) > LocationMapDefinition.AspectTolerance * worldAspect)
                throw new ArgumentException($"A {size.x}x{size.y} image cannot match a {bounds.width}x{bounds.height} m rectangle (aspect {imageAspect:0.####} against {worldAspect:0.####}); use a larger image or a squarer rectangle.", nameof(bounds));
            return size;
        }

        /// <summary>Stands <paramref name="camera"/> over the centre of the rectangle looking straight down, with world +Z up the image, so one pixel is a fixed number of metres and the frustum is exactly the rectangle.</summary>
        public static void FrameCamera(UCamera camera, Rect bounds, float height, float farClip)
        {
            camera.orthographic = true;
            camera.orthographicSize = bounds.height * 0.5f;   // half the vertical extent; the width follows the render target's aspect
            camera.aspect = bounds.width / bounds.height;
            camera.transform.position = new Vector3(bounds.center.x, height, bounds.center.y);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = farClip;
        }

        /// <summary>Renders the open scene over the rectangle and returns a readable, uncompressed, north-up texture (the caller owns and destroys it).</summary>
        public static Texture2D Render(MapBakeOptions options)
        {
            var size = PixelSize(options.WorldBoundsXZ, options.LongSidePixels);
            var go = new GameObject("MapBakeCamera") { hideFlags = HideFlags.HideAndDontSave };
            var target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = Mathf.Max(1, options.AntiAliasing) };
            var previous = RenderTexture.active;
            UCamera camera = null;
            try
            {
                camera = go.AddComponent<UCamera>();
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = options.Background; camera.cullingMask = options.CullingMask;
                camera.allowHDR = false; camera.allowMSAA = options.AntiAliasing > 1; camera.useOcclusionCulling = false;
                FrameCamera(camera, options.WorldBoundsXZ, options.CameraHeight, options.FarClip);
                options.ConfigureCamera?.Invoke(camera);
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                var texture = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false, false) { name = "Baked location map" };
                texture.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                texture.Apply(false, false);
                return texture;
            }
            finally
            {
                RenderTexture.active = previous;
                if (camera != null) camera.targetTexture = null;   // a render texture still set as a target must not be released
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        /// <summary>Writes a PNG under the project's Assets and imports it as a crisp, uncompressed, clamped, mip-less sRGB texture (a map is read at one scale, and its edges must stay sharp). Returns the imported asset.</summary>
        public static Texture2D SavePng(Texture2D texture, string assetPath)
        {
            if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal) || !assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("An asset path under Assets/ ending in .png.", nameof(assetPath));
            var full = Path.GetFullPath(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllBytes(full, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Default; importer.sRGBTexture = true; importer.mipmapEnabled = false; importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear; importer.textureCompression = TextureImporterCompression.Uncompressed; importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = Mathf.Max(texture.width, texture.height) > 4096 ? 8192 : 4096; importer.alphaIsTransparency = false; importer.isReadable = false;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
        }

        /// <summary>Creates or updates the <see cref="LocationMapDefinition"/> asset beside the image, validated: an invalid definition is never written.</summary>
        public static LocationMapDefinition WriteDefinition(string assetPath, string locationId, string title, Texture2D background, Rect worldBoundsXZ, string captureSource)
        {
            var created = LocationMapDefinition.Create(locationId, title, background, worldBoundsXZ, null, captureSource);
            var problems = new List<string>();
            if (!created.Validate(problems)) { UnityEngine.Object.DestroyImmediate(created); throw new InvalidOperationException("Not a valid map definition:\n - " + string.Join("\n - ", problems)); }
            var existing = AssetDatabase.LoadAssetAtPath<LocationMapDefinition>(assetPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(created, existing);
                UnityEngine.Object.DestroyImmediate(created);
                EditorUtility.SetDirty(existing); AssetDatabase.SaveAssetIfDirty(existing);
                return existing;
            }

            AssetDatabase.CreateAsset(created, assetPath);
            return created;
        }

        /// <summary>One call for the whole set: renders, writes the guide image (the raw render with a metre grid, for a painter to paint over at the exact same rectangle), stylises, writes the map image and its definition. Returns the definition.</summary>
        public static LocationMapDefinition Bake(MapBakeOptions options, string folder, string name, string locationId, string title, string captureSource, MapStyle style = null, float guideGridMetres = 50f)
        {
            var raw = Render(options);
            try
            {
                var guide = MapStylise.WithGrid(raw, options.WorldBoundsXZ, guideGridMetres);
                SavePng(guide, $"{folder}/{name}_guide.png"); UnityEngine.Object.DestroyImmediate(guide);
                var styled = style == null ? raw : MapStylise.Parchment(raw, style);
                var image = SavePng(styled, $"{folder}/{name}.png");
                if (!ReferenceEquals(styled, raw)) UnityEngine.Object.DestroyImmediate(styled);
                return WriteDefinition($"{folder}/{name}.asset", locationId, title, image, options.WorldBoundsXZ, captureSource);
            }
            finally { UnityEngine.Object.DestroyImmediate(raw); }
        }
    }
}
