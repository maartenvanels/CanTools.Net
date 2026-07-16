namespace CanTools;

/// <summary>Base class for all errors raised by this library.</summary>
public class CanToolsException : Exception
{
    public CanToolsException(string message)
        : base(message)
    {
    }

    public CanToolsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
