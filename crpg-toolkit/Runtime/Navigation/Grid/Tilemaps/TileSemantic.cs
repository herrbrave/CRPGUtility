namespace CRPG.Navigation.Grid.Tilemaps
{
    /// <summary>
    /// What a painted tile MEANS to the game, independent of how it looks. There is deliberately no default: <see cref="Unclassified"/> is the
    /// zero value so a freshly created tile asset is invalid until someone decides, and the validator and the location baker reject it. Guessing
    /// (a tile that looks like floor but is a wall) is silent until played, so it is not allowed to happen by omission.
    /// <list type="bullet">
    /// <item><b>Ground</b> -- walkable, flat, drawn by a ground tilemap. Opaque floors and transparent edge/decal pieces alike (paint decals on a second
    /// ground tilemap above the base).</item>
    /// <item><b>Blocked</b> -- unwalkable, flat, drawn by a ground tilemap; does not block sight (water, a chasm).</item>
    /// <item><b>Wall</b> -- unwalkable and blocks line of sight; a depth-sorted object.</item>
    /// <item><b>Prop</b> -- unwalkable, depth-sorted object (a tree, a barrel); does not block sight by itself.</item>
    /// <item><b>Decor</b> -- WALKABLE, depth-sorted object (a lantern or bones the party walks past).</item>
    /// <item><b>Door</b> -- a depth-sorted object whose cell is opened and closed by a walkability blocker (which the baker places).</item>
    /// </list>
    /// </summary>
    public enum TileSemantic
    {
        Unclassified = 0,
        Ground = 1,
        Blocked = 2,
        Wall = 3,
        Prop = 4,
        Decor = 5,
        Door = 6,
    }

    public static class TileSemanticExtensions
    {
        /// <summary>True for every semantic that makes its cell unwalkable from the start. Doors are NOT included: their cell is owned by a blocker that
        /// starts closed and can open.</summary>
        public static bool BlocksMovement(this TileSemantic semantic) =>
            semantic == TileSemantic.Blocked || semantic == TileSemantic.Wall || semantic == TileSemantic.Prop;

        /// <summary>True when the tile blocks line of sight (walls only: props and doors are cover decisions left to the content).</summary>
        public static bool BlocksSight(this TileSemantic semantic) => semantic == TileSemantic.Wall;

        /// <summary>True when the tile is drawn as its own depth-sorted object rather than by the flat ground tilemap renderer.</summary>
        public static bool IsSortedObject(this TileSemantic semantic) =>
            semantic == TileSemantic.Wall || semantic == TileSemantic.Prop || semantic == TileSemantic.Decor || semantic == TileSemantic.Door;
    }
}
