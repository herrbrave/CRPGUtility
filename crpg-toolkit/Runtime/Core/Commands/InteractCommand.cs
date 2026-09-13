namespace CRPG.Core
{
    public readonly struct InteractCommand
    {
        public InteractCommand(RuntimeEntityId target)
        {
            Target = target;
        }

        public RuntimeEntityId Target { get; }
    }
}
