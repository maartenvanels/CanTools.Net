namespace CanTools;

/// <summary>The fields of a 29-bit J1939 frame id.</summary>
public readonly record struct J1939FrameId(
    int Priority,
    int Reserved,
    int DataPage,
    int PduFormat,
    int PduSpecific,
    int SourceAddress);
