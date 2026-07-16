namespace CanTools.CanOpen;

/// <summary>Shared formatting of the frame codecs' error and display texts.</summary>
internal static class CanOpenFrames
{
    public static DecodeException WrongLength(string frame, string expected, int actual) =>
        new($"{frame} frame has {expected}, but got {actual}.");

    // matches python-canopen's "Code 0x…, Description" formatting
    public static string Describe(string code, string description) =>
        description.Length == 0 ? code : $"{code}, {description}";
}
