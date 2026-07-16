namespace CanTools;

/// <summary>The fields of a J1939 parameter group number.</summary>
public readonly record struct J1939Pgn(
    int Reserved,
    int DataPage,
    int PduFormat,
    int PduSpecific);
