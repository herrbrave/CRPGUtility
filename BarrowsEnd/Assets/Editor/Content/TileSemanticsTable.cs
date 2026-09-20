using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CRPG.Navigation.Grid.Tilemaps;
using UnityEngine;

namespace BarrowsEnd.Editor
{
    /// <summary>
    /// The curated meaning of the Kenney tiles, read from <c>Assets/Content/Tiles/TileSemantics.json</c>: per sheet, rectangles of sheet cells with a
    /// <see cref="TileSemantic"/>. Later rules override earlier ones. A cell no rule covers is <see cref="TileSemantic.Unclassified"/> and gets no tile
    /// (there is no default meaning). The same file feeds the tile generator, the tests and the colour review overlays.
    /// </summary>
    public sealed class TileSemanticsTable
    {
        public const string DefaultPath = "Assets/Content/Tiles/TileSemantics.json";

        [Serializable] private sealed class RuleData { public string semantic; public int[] cols; public int[] rows; public string note; }
        [Serializable] private sealed class SheetData { public string key; public string path; public string spritePrefix; public RuleData[] rules; }
        [Serializable] private sealed class TableData { public string comment; public SheetData[] sheets; }

        public sealed class Rule
        {
            public TileSemantic Semantic;
            public int ColMin, ColMax, RowMin, RowMax;
            public string Note;
            public bool Covers(int col, int row) => col >= ColMin && col <= ColMax && row >= RowMin && row <= RowMax;
        }

        public sealed class Sheet
        {
            public string Key;
            public string Path;
            public string SpritePrefix;
            public List<Rule> Rules = new();
        }

        public IReadOnlyList<Sheet> Sheets { get; private set; }

        public static TileSemanticsTable Load(string path = DefaultPath)
        {
            var data = JsonUtility.FromJson<TableData>(File.ReadAllText(path));
            if (data?.sheets == null || data.sheets.Length == 0) throw new InvalidDataException($"{path} has no sheets.");

            var sheets = new List<Sheet>();
            foreach (var s in data.sheets)
            {
                var sheet = new Sheet { Key = s.key, Path = s.path, SpritePrefix = s.spritePrefix };
                if (string.IsNullOrEmpty(sheet.Key) || string.IsNullOrEmpty(sheet.Path) || string.IsNullOrEmpty(sheet.SpritePrefix))
                    throw new InvalidDataException($"{path}: every sheet needs a key, a path and a spritePrefix.");

                for (var i = 0; i < (s.rules?.Length ?? 0); i++)
                {
                    var r = s.rules[i];
                    if (!Enum.TryParse<TileSemantic>(r.semantic, out var semantic) || semantic == TileSemantic.Unclassified)
                        throw new InvalidDataException($"{path}: sheet '{sheet.Key}' rule {i} has an invalid semantic '{r.semantic}'.");
                    if (r.cols == null || r.cols.Length != 2 || r.rows == null || r.rows.Length != 2 || r.cols[0] > r.cols[1] || r.rows[0] > r.rows[1])
                        throw new InvalidDataException($"{path}: sheet '{sheet.Key}' rule {i} needs cols [min, max] and rows [min, max] with min <= max.");
                    sheet.Rules.Add(new Rule { Semantic = semantic, ColMin = r.cols[0], ColMax = r.cols[1], RowMin = r.rows[0], RowMax = r.rows[1], Note = r.note });
                }

                sheets.Add(sheet);
            }

            return new TileSemanticsTable { Sheets = sheets };
        }

        public Sheet SheetByKey(string key) => Sheets.First(s => s.Key == key);

        /// <summary>The semantic of a sheet cell: the LAST rule that covers it, or Unclassified.</summary>
        public static TileSemantic Resolve(Sheet sheet, int col, int row)
        {
            var semantic = TileSemantic.Unclassified;
            foreach (var rule in sheet.Rules)
                if (rule.Covers(col, row)) semantic = rule.Semantic;
            return semantic;
        }

        public TileSemantic Resolve(string sheetKey, int col, int row) => Resolve(SheetByKey(sheetKey), col, row);

        /// <summary>Parses "&lt;prefix&gt;_&lt;col&gt;_&lt;row&gt;" (the sliced sprite naming) into a cell.</summary>
        public static bool TryParseCell(Sheet sheet, string spriteName, out int col, out int row)
        {
            col = row = 0;
            var head = sheet.SpritePrefix + "_";
            if (!spriteName.StartsWith(head, StringComparison.Ordinal)) return false;
            var parts = spriteName.Substring(head.Length).Split('_');
            return parts.Length == 2 && int.TryParse(parts[0], out col) && int.TryParse(parts[1], out row);
        }
    }
}
