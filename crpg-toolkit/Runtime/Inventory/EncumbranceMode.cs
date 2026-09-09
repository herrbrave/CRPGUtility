namespace CRPG.Inventory
{
    public enum EncumbranceMode
    {
        Disabled,
        PartyCombined,
        PerCharacter,

        /// <summary>Not a concrete calculator -- author your own IEncumbranceCalculator.</summary>
        Custom
    }
}
