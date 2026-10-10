using CRPG.Actors;
using UnityEditor;
using UnityEngine.UIElements;

namespace CRPG.Editor.Npc
{
    /// <summary>
    /// An extension point for <see cref="NpcDefinitionEditor"/>: a project's own appearance creator (a 3D character builder, a sprite paper doll, ...). The toolkit cannot know what an
    /// NPC's appearance options are (they depend on the art), only that <see cref="NpcDefinition.Appearance"/> holds (slot, option) id pairs, so the creator UI is supplied by whichever
    /// project has one. Implementations are found with <c>TypeCache</c> (a public parameterless constructor is required), asked whether they handle the NPC being inspected, and their element is
    /// added directly after the raw Appearance foldout (which stays: it is the data). If the element implements <see cref="System.IDisposable"/> it is disposed when the inspector goes away or
    /// is rebuilt (a preview render utility, for example, must be cleaned up).
    /// <para>The inspector rebuilds its extensions whenever the NPC's <c>visualPrefab</c> changes, so <see cref="Handles"/> is asked again. Both methods are called inside a try/catch (a
    /// faulty creator never breaks the inspector) and must cope with an NPC that has no visual prefab yet.</para>
    /// </summary>
    public interface INpcAppearanceEditor
    {
        /// <summary>Whether this creator applies to the NPC. A new NPC has no visual prefab: a creator that supplies one answers true for it.</summary>
        bool Handles(NpcDefinition npc);

        /// <summary>The creator UI, bound to the NPC's serialized object (write changes through it so undo and the raw foldout stay correct).</summary>
        VisualElement Create(SerializedObject npc);
    }
}
