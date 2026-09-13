namespace CRPG.Saving
{
    /// <summary>UI-facing seam for requesting a save, implemented by CRPG.Gameplay's SaveRequestService.
    /// Lives in CRPG.Saving (beneath both CRPG.UI and CRPG.Gameplay) specifically so a CRPG.UI SavePresenter
    /// can depend on this port instead of on SaveRequestService directly -- CRPG.Gameplay already references
    /// CRPG.UI, so CRPG.UI referencing CRPG.Gameplay back would be a cycle.</summary>
    public interface ISaveRequestPort
    {
        SaveRequestResult RequestSave(string path);
    }
}
