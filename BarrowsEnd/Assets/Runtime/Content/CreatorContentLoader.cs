using System.Collections.Generic;
using System.Linq;
using CRPG.Core;
using CRPG.Presentation.Creator;

namespace BarrowsEnd
{
    /// <summary>
    /// Maps the campaign's two creator JSON files (<c>CreatorPicks.json</c>, <c>CreatorClassTable.json</c>, Task 4.0) onto the toolkit's plain-data
    /// <see cref="CreatorContent"/>, which <see cref="CharacterCreatorModel"/> consumes. Pure: takes the JSON text (a runtime caller reads it from a
    /// serialized TextAsset, never by path). Skin/Hair/Outfit drive the body/hair/clothes layers; a class's weapons are named from the weapon picks.
    /// Validity of the content against the ruleset is the job of <c>CreatorContentTests</c>, not this mapper.
    /// </summary>
    public static class CreatorContentLoader
    {
        private static readonly DefinitionId BodyLayer = new("layer-body");
        private static readonly DefinitionId HairLayer = new("layer-hair");
        private static readonly DefinitionId ClothesLayer = new("layer-clothes");

        public static CreatorContent Build(string picksJson, string tableJson) =>
            Build(CreatorPicks.Parse(picksJson), CreatorClassTable.Parse(tableJson));

        public static CreatorContent Build(CreatorPicks picks, CreatorClassTable table)
        {
            var rows = new[]
            {
                new CreatorAppearanceRowSpec(BodyLayer, "Skin", Options(picks.skin)),
                new CreatorAppearanceRowSpec(HairLayer, "Hair", Options(picks.hair)),
                new CreatorAppearanceRowSpec(ClothesLayer, "Outfit", Options(picks.outfit)),
            };

            var weaponNames = new Dictionary<string, string>();
            foreach (var pick in picks.weapons)
                if (!string.IsNullOrEmpty(pick.item)) weaponNames[pick.item] = pick.name;

            var classes = table.classes.Select(entry => new CreatorClassSpec(
                new DefinitionId(entry.classId),
                entry.weapons.Select(item => new CreatorOption(new DefinitionId(item), weaponNames.TryGetValue(item, out var name) ? name : item)),
                entry.scoreOrder.Select(id => new DefinitionId(id)),
                entry.choiceDefaults.Select(d => new CreatorChoiceSpec(d.requirementId, d.options.Select(o => new DefinitionId(o))))));

            return new CreatorContent(new DefinitionId(table.race), new DefinitionId(table.background), table.level, table.standardArray, rows, classes);
        }

        private static IEnumerable<CreatorOption> Options(CreatorPick[] picks) =>
            picks.Select(p => new CreatorOption(new DefinitionId(p.option), p.name));
    }
}
