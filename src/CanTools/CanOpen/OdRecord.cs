namespace CanTools.CanOpen;

/// <summary>An object dictionary record: named members at explicit subindexes.</summary>
public sealed class OdRecord : OdComposite
{
    public OdRecord(int index, string name)
        : base(index, name)
    {
    }
}
