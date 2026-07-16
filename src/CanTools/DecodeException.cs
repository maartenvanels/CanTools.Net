namespace CanTools;

/// <summary>Thrown when frame data cannot be decoded into signal values.</summary>
public class DecodeException : CanToolsException
{
    public DecodeException(string message)
        : base(message)
    {
    }

    public DecodeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
