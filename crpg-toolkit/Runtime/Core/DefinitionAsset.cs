using UnityEngine;

namespace CRPG.Core
{
    /// <summary>
    /// Base class for authored ScriptableObject definitions. Assigns a stable <see cref="DefinitionId"/> once,
    /// the first time the asset is saved, independent of Unity's own asset GUID.
    /// </summary>
    /// <remarks>
    /// Duplicating an asset in the editor (Ctrl+D) copies this id along with every other serialized field,
    /// which produces two definitions sharing one id. That is caught by the project validator
    /// (Editor/Validation, "duplicate GUIDs") rather than prevented here.
    /// </remarks>
    public abstract class DefinitionAsset : ScriptableObject, IDefinition
    {
        [SerializeField, HideInInspector] private string id;

        public DefinitionId Id => new DefinitionId(id);

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            if (string.IsNullOrEmpty(id))
            {
                id = System.Guid.NewGuid().ToString("N");
                UnityEditor.EditorUtility.SetDirty(this);
            }
        }
#endif
    }
}
