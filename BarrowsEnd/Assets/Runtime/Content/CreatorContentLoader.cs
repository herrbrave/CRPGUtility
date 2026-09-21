using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using CRPG.Presentation.Creator;
using UnityEngine;

namespace BarrowsEnd
{
    /// <summary>
    /// Maps the campaign's two creator JSON files (<c>CreatorPicks.json</c>, <c>CreatorClassTable.json</c>) onto the toolkit's plain-data
    /// <see cref="CreatorContent"/>, which <see cref="CharacterCreatorModel"/> consumes. Pure: takes the JSON text (a runtime caller reads it from a
    /// serialized TextAsset, never by path). Skin drives the body layer; Hair is a filtered row (Hair Colour with swatches, then Hair Style) over the hair layer; a
    /// class's starting kit (armor, weapon) is named from the armor and weapon picks. Validity of the content against the ruleset is the job of
    /// <c>CreatorContentTests</c>, not this mapper.
    /// </summary>
    public static class CreatorContentLoader
    {
        public const string HairColorKey = "hair-color";
        private static readonly DefinitionId BodyLayer = new("layer-body");
        private static readonly DefinitionId HairLayer = new("layer-hair");

        public static CreatorContent Build(string picksJson, string tableJson) =>
            Build(CreatorPicks.Parse(picksJson), CreatorClassTable.Parse(tableJson));

        public static CreatorContent Build(CreatorPicks picks, CreatorClassTable table)
        {
            var filters = picks.hairColors.Select(c => new CreatorFilter(c.id, c.name, ParseSwatch(c.swatch), c.options.Select(o => new DefinitionId(o))));
            var rows = new CreatorAppearanceRowSpec[]
            {
                new(BodyLayer, "Skin", picks.skin.Select(p => new CreatorOption(new DefinitionId(p.option), p.name))),
                new CreatorFilteredAppearanceRowSpec(HairLayer, HairColorKey, "Hair Colour", filters, "Hair Style", picks.hairStyleNames),
            };

            var itemNames = new Dictionary<string, string>();
            foreach (var pick in picks.armors.Concat(picks.weapons))
                if (!string.IsNullOrEmpty(pick.item)) itemNames[pick.item] = pick.name;

            var classes = table.classes.Select(entry => new CreatorClassSpec(
                new DefinitionId(entry.classId),
                entry.startingItems.Select(item => new CreatorOption(new DefinitionId(item), itemNames.TryGetValue(item, out var name) ? name : item)),
                entry.scoreOrder.Select(id => new DefinitionId(id)),
                entry.choiceDefaults.Select(d => new CreatorChoiceSpec(d.requirementId, d.options.Select(o => new DefinitionId(o))))));

            return new CreatorContent(new DefinitionId(table.race), new DefinitionId(table.background), table.level, table.standardArray, rows, classes);
        }

        /// <summary>Parses <c>#RRGGBB</c>; an unparseable value becomes magenta so a data mistake is visible instead of silent.</summary>
        public static Color32 ParseSwatch(string hex) =>
            ColorUtility.TryParseHtmlString(hex, out var color) ? (Color32)color : new Color32(255, 0, 255, 255);
    }
}
