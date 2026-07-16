namespace CanTools;

/// <summary>Thrown when signal values cannot be encoded into frame data.</summary>
public class EncodeException : CanToolsException
{
    public EncodeException(string message)
        : base(message)
    {
    }

    public EncodeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
