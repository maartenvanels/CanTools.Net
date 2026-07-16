namespace CanTools.Model;

/// <summary>A CAN bus.</summary>
public sealed class Bus
{
    public Bus(string name, Comments? comment = null, int? baudrate = null, int? fdBaudrate = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Comments = comment;
        Baudrate = baudrate;
        FdBaudrate = fdBaudrate;
    }

    public string Name { get; }

    /// <summary>The bus comment, or null if unavailable.</summary>
    public string? Comment => Comments?.Resolve();

    public Comments? Comments { get; }

    /// <summary>The bus baudrate, or null if unavailable.</summary>
    public int? Baudrate { get; }

    /// <summary>The baudrate used for the payload of CAN FD frames, or null if unavailable.</summary>
    public int? FdBaudrate { get; }

    public override string ToString() => $"Bus {Name}";
}
