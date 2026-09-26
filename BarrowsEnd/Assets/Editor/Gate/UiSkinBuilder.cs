using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BarrowsEnd.Editor
{
    [Serializable]
    public sealed class SkinPartComposite
    {
        public string left;
        public string mid;
        public string right;
    }

    [Serializable]
    public sealed class SkinPartEntry
    {
        public string name;
        public string source; // plain copy, OR:
        public SkinPartComposite composite; // a 3-part horizontal strip (left/mid/right), mutually exclusive with source
        public int[] slice; // [left, top, right, bottom]; all-zero means "unsliced" (drawn at native size, no guard applied)
    }

    [Serializable]
    public sealed class SkinPartManifest
    {
        public SkinPartEntry[] parts;
    }

    /// <summary>
    /// Task 5.0, Phase 5 (see Docs/Plans/2D-Campaign-Phase5-UIReskin.md, Step 2): turns the Kenney Adventure UI pack's
    /// individual PNGs (<see cref="SourceDir"/>) into the skin's own part textures (<see cref="PartsDir"/>) per
    /// <see cref="ManifestPath"/>, force-imports them Point/no-mips/uncompressed/clamp/non-readable, and emits a
    /// generated USS fragment (<see cref="SlicesUssPath"/>) carrying every part's slice geometry so the hand-written
    /// skin never repeats a slice number. Deterministic: identical input produces byte-identical part PNGs; a
    /// re-run that changes nothing skips the write and the reimport (unlike <c>BarrowsEndSetup.SliceSheet</c>, which
    /// always touches mtime). Composited bars are 3-part horizontal strips (left+mid+right) sliced 9/0/9/0, which
    /// covers every target width without generating a wide bitmap per width.
    /// <para><b>NOT under <c>Assets/Art/Source/</c></b> (see the plan's D1a): that tree is
    /// <c>BarrowsEndSetup.SliceAll</c>'s recursive scan root, which force-converts every texture there into a 16px-grid
    /// multi-sprite sheet -- these UI parts are not 16px-grid sprites and would be silently wrecked.</para>
    /// </summary>
    public static class UiSkinBuilder
    {
        public const string SourceDir = "Assets/Art/UI/Source/PNG";
        public const string PartsDir = "Assets/Art/UI/Parts";
        public const string ManifestPath = "Assets/UI/Skin/SkinParts.json";
        public const string SlicesUssPath = "Assets/UI/Skin/skin-slices.uss";

        [MenuItem("Barrows End/UI/Build Skin Parts")]
        public static void Build()
        {
            var result = BuildFromManifest();
            Debug.Log($"[UiSkinBuilder] built {result.PartsWritten} of {result.PartsTotal} part(s) ({result.PartsTotal - result.PartsWritten} unchanged, skipped).");
        }

        public readonly struct BuildResult
        {
            public readonly int PartsTotal;
            public readonly int PartsWritten;
            public readonly IReadOnlyDictionary<string, string> PartPaths; // part name -> "Assets/..." path

            public BuildResult(int total, int written, IReadOnlyDictionary<string, string> partPaths)
            {
                PartsTotal = total;
                PartsWritten = written;
                PartPaths = partPaths;
            }
        }

        public static BuildResult BuildFromManifest() => BuildFromManifest(LoadManifest());

        /// <summary>Overload taking an explicit manifest -- lets tests exercise validation (missing source,
        /// duplicate name, a slice too large) against a throwaway manifest without touching the real one.</summary>
        public static BuildResult BuildFromManifest(SkinPartManifest manifest)
        {
            ValidateNoDuplicateNames(manifest);

            Directory.CreateDirectory(PartsDir);
            var written = 0;
            var partPaths = new Dictionary<string, string>();
            var sliceLines = new List<string>();

            foreach (var entry in manifest.parts)
            {
                var slice = entry.slice ?? new[] { 0, 0, 0, 0 };
                if (slice.Length != 4) throw new InvalidOperationException($"Part '{entry.name}': slice must have exactly 4 values [left, top, right, bottom].");

                // JsonUtility default-constructs a nested [Serializable] class field even when the JSON key is
                // absent, so a plain-copy entry still gets a non-null (but empty) `composite` -- check for an
                // actually-populated sub-field, not just non-null.
                var isComposite = entry.composite != null && !string.IsNullOrEmpty(entry.composite.left);
                byte[] bytes = isComposite ? BuildComposite(entry) : ReadSource(entry.name, entry.source);
                var destPath = $"{PartsDir}/{entry.name}.png";
                partPaths[entry.name] = destPath;

                // Validate the slice leaves a real stretchable middle -- not a full correctness oracle (that's the
                // render test, PlayMode test 8 in the plan), just a sanity backstop against nonsense values.
                var isSliced = slice[0] != 0 || slice[1] != 0 || slice[2] != 0 || slice[3] != 0;
                if (isSliced)
                {
                    var (w, h) = PngSize(bytes);
                    if (slice.Any(s => s < 0)) throw new InvalidOperationException($"Part '{entry.name}': slice values must not be negative.");
                    if (slice[0] + slice[2] >= w) throw new InvalidOperationException($"Part '{entry.name}': left+right slice ({slice[0]}+{slice[2]}) leaves no stretchable width ({w}px wide).");
                    if (slice[1] + slice[3] >= h) throw new InvalidOperationException($"Part '{entry.name}': top+bottom slice ({slice[1]}+{slice[3]}) leaves no stretchable height ({h}px tall).");
                }

                if (WriteIfChanged(destPath, bytes)) written++;
                ForceImporterSettings(destPath);

                sliceLines.Add(isSliced
                    ? $".be-part--{entry.name} {{ background-image: url(\"/{destPath}\"); -unity-slice-left: {slice[0]}; -unity-slice-top: {slice[1]}; -unity-slice-right: {slice[2]}; -unity-slice-bottom: {slice[3]}; -unity-slice-scale: 1px; }}"
                    : $".be-part--{entry.name} {{ background-image: url(\"/{destPath}\"); }}");
            }

            var fragment = "/* GENERATED by UiSkinBuilder.Build() -- do not hand-edit. Slice numbers live only in SkinParts.json. */\n" + string.Join("\n", sliceLines) + "\n";
            if (WriteTextIfChanged(SlicesUssPath, fragment)) written++;

            AssetDatabase.Refresh();
            AssetDatabase.SaveAssets();
            return new BuildResult(manifest.parts.Length, written, partPaths);
        }

        public static SkinPartManifest LoadManifest()
        {
            if (!File.Exists(ManifestPath)) throw new InvalidOperationException($"{ManifestPath} is missing.");
            var manifest = JsonUtility.FromJson<SkinPartManifest>(File.ReadAllText(ManifestPath));
            if (manifest?.parts == null || manifest.parts.Length == 0) throw new InvalidOperationException($"{ManifestPath} has no parts.");
            return manifest;
        }

        private static void ValidateNoDuplicateNames(SkinPartManifest manifest)
        {
            var seen = new HashSet<string>();
            foreach (var entry in manifest.parts)
            {
                if (string.IsNullOrEmpty(entry.name)) throw new InvalidOperationException("A manifest entry has no name.");
                if (!seen.Add(entry.name)) throw new InvalidOperationException($"Duplicate part name '{entry.name}'.");
            }
        }

        private static byte[] ReadSource(string partName, string sourceFileName)
        {
            if (string.IsNullOrEmpty(sourceFileName)) throw new InvalidOperationException($"Part '{partName}' has neither 'source' nor 'composite'.");
            var path = $"{SourceDir}/{sourceFileName}";
            if (!File.Exists(path)) throw new InvalidOperationException($"Part '{partName}': source '{path}' is missing from the pack copy.");
            return File.ReadAllBytes(path);
        }

        private static byte[] BuildComposite(SkinPartEntry entry)
        {
            var left = LoadTexture(entry.name, entry.composite.left);
            var mid = LoadTexture(entry.name, entry.composite.mid);
            var right = LoadTexture(entry.name, entry.composite.right);
            var h = Mathf.Max(left.height, mid.height, right.height);
            var w = left.width + mid.width + right.width;

            var strip = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var clear = Enumerable.Repeat(new Color32(0, 0, 0, 0), w * h).ToArray();
            strip.SetPixels32(clear);
            BlitInto(strip, left, 0);
            BlitInto(strip, mid, left.width);
            BlitInto(strip, right, left.width + mid.width);
            strip.Apply();

            var bytes = strip.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(strip);
            UnityEngine.Object.DestroyImmediate(left);
            UnityEngine.Object.DestroyImmediate(mid);
            UnityEngine.Object.DestroyImmediate(right);
            return bytes;
        }

        private static Texture2D LoadTexture(string partName, string sourceFileName)
        {
            var bytes = ReadSource(partName, sourceFileName);
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes)) throw new InvalidOperationException($"Part '{partName}': '{sourceFileName}' failed to decode as a PNG.");
            return tex;
        }

        private static void BlitInto(Texture2D dest, Texture2D src, int destX)
        {
            for (var y = 0; y < src.height; y++)
            for (var x = 0; x < src.width; x++)
                dest.SetPixel(destX + x, y, src.GetPixel(x, y));
        }

        private static (int w, int h) PngSize(byte[] bytes)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(bytes);
            var size = (tex.width, tex.height);
            UnityEngine.Object.DestroyImmediate(tex);
            return size;
        }

        private static bool WriteIfChanged(string path, byte[] bytes)
        {
            if (File.Exists(path) && File.ReadAllBytes(path).SequenceEqual(bytes)) return false;
            File.WriteAllBytes(path, bytes);
            return true;
        }

        private static bool WriteTextIfChanged(string path, string text)
        {
            if (File.Exists(path) && File.ReadAllText(path) == text) return false;
            File.WriteAllText(path, text);
            return true;
        }

        /// <summary>Explicit every run, but SaveAndReimport is skipped when nothing actually changed (unlike
        /// BarrowsEndSetup.SliceSheet, which always touches mtime) -- avoids needless reimport churn on a no-op rebuild.</summary>
        private static void ForceImporterSettings(string path)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUncompressedImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null) throw new InvalidOperationException($"'{path}' did not import as a texture.");

            var changed = importer.textureType != TextureImporterType.Default
                          || importer.filterMode != FilterMode.Point
                          || importer.mipmapEnabled
                          || importer.textureCompression != TextureImporterCompression.Uncompressed
                          || !importer.sRGBTexture
                          || importer.wrapMode != TextureWrapMode.Clamp
                          || !importer.alphaIsTransparency
                          || importer.isReadable;

            if (!changed) return;

            importer.textureType = TextureImporterType.Default;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.isReadable = false;
            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
        }
    }
}
