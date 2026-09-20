using UnityEngine;
using UnityEngine.Tilemaps;

namespace CRPG.Navigation.Grid.Tilemaps
{
    /// <summary>
    /// A <see cref="Tile"/> that carries a <see cref="TileSemantic"/>: what the painted cell means (walkable? blocks sight? drawn as a sorted
    /// object?). Ruleset-agnostic. The game's tilemap authoring pipeline (paint on a Tilemap on the XZ plane, bake the walkability grid and colliders)
    /// reads the semantic; nothing here knows about a particular art set.
    /// <para><b>Rendering.</b> A tilemap renderer has ONE sorting order for the whole map, so anything that must depth-sort against characters cannot be
    /// drawn by it. A tile whose semantic <see cref="TileSemanticExtensions.IsSortedObject"/> therefore points <see cref="Tile.gameObject"/> at a small
    /// shared prefab (a SpriteRenderer with a static depth sorter). At runtime the Tilemap instantiates it at the cell, already flat on the XZ plane;
    /// <see cref="StartUp"/> gives the instance this tile's sprite, and <see cref="GetTileData"/> hides the tilemap's own copy of the sprite so nothing
    /// draws twice. In the editor (painting) the tile's own sprite is what you see, and the prefab is not instantiated
    /// (<see cref="TileFlags.InstantiateGameObjectRuntimeOnly"/>).</para>
    /// </summary>
    [CreateAssetMenu(menuName = "CRPG Toolkit/Navigation/Semantic Tile")]
    public sealed class SemanticTile : Tile
    {
        [SerializeField] private TileSemantic semantic;

        public TileSemantic Semantic => semantic;
        public bool IsClassified => semantic != TileSemantic.Unclassified;
        public bool BlocksMovement => semantic.BlocksMovement();
        public bool BlocksSight => semantic.BlocksSight();
        public bool IsSortedObject => semantic.IsSortedObject();

        /// <summary>Sets the meaning, the sprite and (for a sorted-object semantic) the object prefab in one go, keeping the flags consistent. Used by tile
        /// generators; a hand-authored tile can just use the inspector.</summary>
        public void Configure(TileSemantic newSemantic, Sprite newSprite, GameObject objectPrefab)
        {
            semantic = newSemantic;
            sprite = newSprite;
            colliderType = ColliderType.None;
            gameObject = newSemantic.IsSortedObject() ? objectPrefab : null;
            flags = newSemantic.IsSortedObject() ? TileFlags.LockColor | TileFlags.InstantiateGameObjectRuntimeOnly : TileFlags.LockColor;
        }

        public override void GetTileData(Vector3Int position, ITilemap tilemap, ref TileData tileData)
        {
            base.GetTileData(position, tilemap, ref tileData);

            // While playing, the instantiated object draws the sprite; the tilemap must not draw a second copy underneath it.
            if (Application.isPlaying && IsSortedObject && gameObject != null) tileData.sprite = null;
        }

        public override bool StartUp(Vector3Int position, ITilemap tilemap, GameObject go)
        {
            if (go != null && sprite != null)
            {
                var renderer = go.GetComponent<SpriteRenderer>();
                if (renderer == null) renderer = go.GetComponentInChildren<SpriteRenderer>(true);
                if (renderer != null) renderer.sprite = sprite;
            }

            return base.StartUp(position, tilemap, go);
        }
    }
}
