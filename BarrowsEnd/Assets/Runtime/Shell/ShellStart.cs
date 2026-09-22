namespace BarrowsEnd.Shell
{
    /// <summary>How <see cref="ShellBootstrap"/> begins: <see cref="AuthoredParty"/> boots straight into the town with the authored party (dev and the existing
    /// shell tests); <see cref="Title"/> shows the title screen (New Game runs the creator, Continue loads the save). The authored Core scene uses Title.</summary>
    public enum ShellStartMode
    {
        AuthoredParty = 0,
        Title = 1,
    }

    /// <summary>Where the shell is in its start flow, observable for tests: Booting (before the start flow), Title, Creating (the creator is shown),
    /// Loading (Continue's load is in flight) and Playing.</summary>
    public enum ShellStartPhase
    {
        Booting = 0,
        Title = 1,
        Creating = 2,
        Loading = 3,
        Playing = 4,
    }
}
