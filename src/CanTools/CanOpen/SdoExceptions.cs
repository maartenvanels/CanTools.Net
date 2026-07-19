namespace CanTools.CanOpen;

/// <summary>Base class for errors raised while running an SDO transfer.</summary>
public class SdoException : CanToolsException
{
    public SdoException(string message)
        : base(message)
    {
    }
}

/// <summary>No response arrived within the configured timeout.</summary>
public sealed class SdoTimeoutException : SdoException
{
    public SdoTimeoutException(ushort index, byte subIndex)
        : base($"SDO transfer of 0x{index:X4}sub{subIndex:X} timed out.")
    {
        Index = index;
        Subindex = subIndex;
    }

    public ushort Index { get; }

    public byte Subindex { get; }
}

/// <summary>The server aborted the transfer.</summary>
public sealed class SdoAbortException : SdoException
{
    public SdoAbortException(ushort index, byte subIndex, SdoAbortCode code)
        : base($"SDO transfer of 0x{index:X4}sub{subIndex:X} aborted: "
               + $"0x{(uint)code:X8} {code.Description()}".TrimEnd())
    {
        Index = index;
        Subindex = subIndex;
        Code = code;
    }

    public ushort Index { get; }

    public byte Subindex { get; }

    public SdoAbortCode Code { get; }
}

/// <summary>The peer violated the SDO protocol (wrong specifier, toggle mismatch).</summary>
public sealed class SdoProtocolException : SdoException
{
    public SdoProtocolException(string message)
        : base(message)
    {
    }
}
