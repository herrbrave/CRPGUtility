namespace CRPG.Core
{
    public enum SelectionMode
    {
        /// <summary>Click, drag-box, Select All: the given characters become the entire selection.</summary>
        Replace,

        /// <summary>Shift-click: each given character's membership in the selection is toggled.</summary>
        Toggle
    }
}
