using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace VikingRaid.Appearance.Editor
{
    /// <summary>Diagnostic for script-generated colour maps: which cells of the 32x32 Sidekick colour map does each part group sample (skin, hair, eyes, teeth, clothing), and how do the eight shipped maps differ in those cells? Writes <c>VikingRaid_Logs/spikes/palette_cells.txt</c>.</summary>
    public static class PaletteCellProbe
    {
        private const string Humans = "Assets/Synty/SidekickCharacters/Resources/Meshes/Species/Humans";
        private const string Starter = "Assets/Synty/SidekickCharacters/Resources/Meshes/Outfits/Starter";
        private const string Chars = "Assets/Synty/SidekickCharacters/Characters";

        private static HashSet<(int x, int y)> Cells(params string[] paths)
        {
            var cells = new HashSet<(int, int)>();
            foreach (var path in paths)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    foreach (var uv in smr.sharedMesh.uv) cells.Add((Mathf.Clamp(Mathf.FloorToInt(uv.x * 32f), 0, 31), Mathf.Clamp(Mathf.FloorToInt(uv.y * 32f), 0, 31)));
            }

            return cells;
        }

        private static string Fmt(IEnumerable<(int x, int y)> cells) => string.Join(" ", cells.OrderBy(c => c.y).ThenBy(c => c.x).Select(c => $"({c.x},{c.y})"));

        public static void Run()
        {
            var report = new StringBuilder();
            string B(string r) => $"{Humans}/SK_HUMN_BASE_01_{r}_HU01.fbx";
            string S(int n, string r) => $"{Humans}/SK_HUMN_BASE_{n:00}_{r}_HU01.fbx";
            var bodyRegions = new[] { "10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "15HNDL", "16HNDR", "17HIPS", "18LEGL", "19LEGR", "20FOTL", "21FOTR" };
            var groups = new (string name, HashSet<(int x, int y)> cells)[]
            {
                ("HEAD01", Cells(B("01HEAD"))),
                ("EYES", Cells(B("05EYEL"), B("06EYER"))),
                ("TONGUE", Cells(B("37TONG"))),
                ("TEETH 1-10", Cells(Enumerable.Range(1, 10).Select(n => S(n, "36TETH")).ToArray())),
                ("BROWS 1-10", Cells(Enumerable.Range(1, 10).SelectMany(n => new[] { S(n, "03EBRL"), S(n, "04EBRR") }).ToArray())),
                ("EARS 1-10", Cells(Enumerable.Range(1, 10).SelectMany(n => new[] { S(n, "07EARL"), S(n, "08EARR") }).ToArray())),
                ("NOSE 1-10", Cells(Enumerable.Range(1, 10).Select(n => S(n, "35NOSE")).ToArray())),
                ("HAIR 1-10", Cells(Enumerable.Range(1, 10).Select(n => S(n, "02HAIR")).ToArray())),
                ("FACIAL HAIR 1-10", Cells(Enumerable.Range(1, 10).Select(n => S(n, "09FCHR")).ToArray())),
                ("BARE BODY (12 regions)", Cells(bodyRegions.Select(B).ToArray())),
                ("BARE TORSO+ARMS+HANDS", Cells(new[] { "10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "15HNDL", "16HNDR" }.Select(B).ToArray())),
                ("BARE HIPS+LEGS+FEET", Cells(new[] { "17HIPS", "18LEGL", "19LEGR", "20FOTL", "21FOTR" }.Select(B).ToArray())),
                ("CIVILIAN outfit (12)", Cells(bodyRegions.Select(r => $"{Starter}/SK_SCFI_CIVL_09_{r}_HU01.fbx").ToArray())),
                ("KNIGHT outfit (12)", Cells(bodyRegions.Select(r => $"{Starter}/SK_FANT_KNGT_17_{r}_HU01.fbx").ToArray())),
                ("TUNIC outfit (ours)", Cells(new[] { "10TORS", "11AUPL", "12AUPR", "13ALWL", "14ALWR", "17HIPS", "18LEGL", "19LEGR", "20FOTL", "21FOTR" }.Select(r => $"Assets/Appearance/SidekickHumans/Outfits/VikingTunic/SK_VKNG_TUNC_01_{r}_HU01.fbx").ToArray())),
            };

            report.AppendLine("== cells sampled per part group (uv x, uv y; y = 0 at the bottom of the texture)");
            foreach (var (name, cells) in groups) report.AppendLine($"{name,-26} {cells.Count,3} cells: {Fmt(cells)}");

            var skin = groups.First(g => g.name == "BARE BODY (12 regions)").cells;
            var head = groups.First(g => g.name == "HEAD01").cells;
            var clothes = groups.First(g => g.name.StartsWith("CIVILIAN")).cells.Union(groups.First(g => g.name.StartsWith("KNIGHT")).cells).ToHashSet();
            report.AppendLine();
            report.AppendLine($"cells used by BOTH the bare body/head and an outfit (a clothing cell that is also skin): {Fmt(skin.Union(head).Intersect(clothes))}");
            report.AppendLine($"cells used by an outfit and NOT by the bare body/head: {clothes.Except(skin.Union(head)).Count()}");

            report.AppendLine();
            report.AppendLine("== the eight shipped maps: colour at selected cells (hex), then how many of the 1024 cells differ from Starter_01");
            var maps = new List<(string name, Color32[] pixels, int width)>();
            foreach (var path in new[] { "HumanSpecies/HumanSpecies_01/Textures/T_HumanSpecies_01ColorMap.png", "HumanSpecies/HumanSpecies_02/Textures/T_HumanSpecies_02ColorMap.png", "HumanSpecies/HumanSpecies_03/Textures/T_HumanSpecies_03ColorMap.png", "HumanSpecies/HumanSpecies_04/Textures/T_HumanSpecies_04ColorMap.png",
                "Starter/Starter_01/Textures/T_Starter_01ColorMap.png", "Starter/Starter_02/Textures/T_Starter_02ColorMap.png", "Starter/Starter_03/Textures/T_Starter_03ColorMap.png", "Starter/Starter_04/Textures/T_Starter_04ColorMap.png" })
            {
                var tex = new Texture2D(2, 2);
                tex.LoadImage(File.ReadAllBytes($"{Chars}/{path}"));
                maps.Add((Path.GetFileNameWithoutExtension(path), tex.GetPixels32(), tex.width));
            }

            string Hex(Color32[] p, int w, (int x, int y) c) { var v = p[c.y * w + c.x]; return $"#{v.r:X2}{v.g:X2}{v.b:X2}"; }
            var reference = maps.First(m => m.name.Contains("Starter_01"));
            var skinOrHead = skin.Union(head).Union(groups.First(g => g.name == "EYES").cells).Union(groups.First(g => g.name == "TEETH 1-10").cells).ToHashSet();
            foreach (var (name, pixels, width) in maps)
            {
                var differing = Enumerable.Range(0, 1024).Count(i => pixels[i].r != reference.pixels[i].r || pixels[i].g != reference.pixels[i].g || pixels[i].b != reference.pixels[i].b);
                var diffSkin = skinOrHead.Count(c => !Hex(pixels, width, c).Equals(Hex(reference.pixels, reference.width, c)));
                var hairCells = groups.First(g => g.name == "HAIR 1-10").cells;
                var diffHair = hairCells.Count(c => !Hex(pixels, width, c).Equals(Hex(reference.pixels, reference.width, c)));
                report.AppendLine($"{name,-26} differs from Starter_01 in {differing,4}/1024 cells; of the {skinOrHead.Count} skin/eye/teeth cells {diffSkin} differ; of the {hairCells.Count} hair cells {diffHair} differ");
            }

            report.AppendLine();
            report.AppendLine("== colours of the key cells in each shipped map");
            var probeCells = new (string label, (int x, int y) cell)[]
            {
                ("skin torso (2,6)", (2, 6)), ("skin torso (4,6)", (4, 6)), ("skin legs (2,4)", (2, 4)), ("skin legs (4,4)", (4, 4)), ("head (4,2)", (4, 2)), ("head (2,8)", (2, 8)), ("head (4,8)", (4, 8)),
                ("(0,10)", (0, 10)), ("(0,11)", (0, 11)), ("ears (2,10)", (2, 10)), ("ears (4,10)", (4, 10)), ("hair (12,2)", (12, 2)), ("hair (12,8)", (12, 8)), ("facial (14,8)", (14, 8)),
                ("brow (12,10)", (12, 10)), ("brow (14,10)", (14, 10)), ("eye (8,2)", (8, 2)), ("eye (10,2)", (10, 2)), ("eye (8,6)", (8, 6)), ("tongue (6,2)", (6, 2)), ("teeth (6,6)", (6, 6)),
            };
            report.AppendLine("cell".PadRight(18) + string.Join(" ", maps.Select(m => m.name.Replace("T_", "").Replace("ColorMap", "").PadRight(15))));
            foreach (var (label, cell) in probeCells)
                report.AppendLine(label.PadRight(18) + string.Join(" ", maps.Select(m => Hex(m.pixels, m.width, cell).PadRight(15))));

            var outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "VikingRaid_Logs", "spikes"));
            File.WriteAllText(Path.Combine(outDir, "palette_cells.txt"), report.ToString());
            Debug.Log("[PaletteCellProbe] done");
        }
    }
}
