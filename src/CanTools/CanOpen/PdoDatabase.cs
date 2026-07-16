using CanTools.Model;

namespace CanTools.CanOpen;

/// <summary>
/// Projects the PDO configuration of an object dictionary onto a plain
/// <see cref="Database"/>, so PDO frames decode with the ordinary CanTools
/// tooling. Follows python-canopen's read-from-OD semantics: DCF ParameterValue
/// beats EDS DefaultValue, disabled PDOs (COB-ID bit 31) are skipped, and mapping
/// entries whose target is missing from the dictionary are dropped.
/// </summary>
public static class PdoDatabase
{
    private const int RpdoCommunicationBase = 0x1400;
    private const int RpdoMappingBase = 0x1600;
    private const int TpdoCommunicationBase = 0x1800;
    private const int TpdoMappingBase = 0x1A00;
    private const int MaximumPdoCount = 512;

    public static Database Create(ObjectDictionary objectDictionary)
    {
        var messages = new List<Message>();

        for (var pdo = 0; pdo < MaximumPdoCount; pdo++)
        {
            AddPdo(messages, objectDictionary, RpdoCommunicationBase + pdo, RpdoMappingBase + pdo);
            AddPdo(messages, objectDictionary, TpdoCommunicationBase + pdo, TpdoMappingBase + pdo);
        }

        return new Database(messages);
    }

    private static void AddPdo(
        List<Message> messages, ObjectDictionary od, int communicationIndex, int mappingIndex)
    {
        if (!od.Contains(communicationIndex))
        {
            return;
        }

        var rawCobId = RequiredParameter(od, communicationIndex, 1);

        // Bit 31 marks the PDO invalid; bit 30 forbids RTR; bit 29 is dropped, like
        // python-canopen. The low 29 bits are the arbitration id.
        if ((rawCobId & 0x80000000) != 0)
        {
            return;
        }

        var cobId = (uint)(rawCobId & 0x1FFFFFFF);
        var signals = new List<Signal>();
        var bitOffset = 0;

        var entryCount = (int)RequiredParameter(od, mappingIndex, 0);

        for (var entry = 1; entry <= entryCount; entry++)
        {
            var value = RequiredParameter(od, mappingIndex, entry);
            var targetIndex = (int)(value >> 16);
            var targetSubindex = (int)(value >> 8) & 0xFF;
            var bitLength = (int)value & 0x7F;

            if (targetIndex == 0 || bitLength == 0)
            {
                continue;
            }

            // A target that is missing from the dictionary skips the entry without
            // advancing the offset, like python-canopen.
            if (LookUpTarget(od, targetIndex, targetSubindex) is not { } target)
            {
                continue;
            }

            signals.Add(BuildSignal(target.Variable, target.QualifiedName, bitOffset, bitLength));
            bitOffset += bitLength;
        }

        messages.Add(new Message(
            frameId: cobId,
            name: PdoName(cobId),
            length: (bitOffset + 7) / 8,
            signals: signals,
            isExtendedFrame: cobId > 0x7FF));
    }

    private static (OdVariable Variable, string QualifiedName)? LookUpTarget(
        ObjectDictionary od, int index, int subindex)
    {
        if (!od.TryGetEntry(index, out var entry))
        {
            return null;
        }

        // A plain variable ignores the subindex, like python-canopen.
        if (entry is OdVariable variable)
        {
            return (variable, variable.Name);
        }

        var member = od.GetVariable(index, subindex);

        return member is null ? null : (member, $"{entry.Name}.{member.Name}");
    }

    private static Signal BuildSignal(
        OdVariable variable, string name, int bitOffset, int bitLength)
    {
        var isFloat = variable.DataType.IsFloat();

        return new Signal(
            name,
            start: bitOffset,
            length: bitLength,
            byteOrder: ByteOrder.LittleEndian,
            isSigned: !isFloat && variable.DataType.IsSigned(),
            conversion: Conversion.Create(isFloat: isFloat),
            minimum: variable.Minimum,
            maximum: variable.Maximum,
            unit: variable.Unit == "" ? null : variable.Unit,
            comment: variable.Description == "" ? null : new Comments(variable.Description));
    }

    // python-canopen's PdoMap.name: the direction from bit 7, the PDO number from
    // the high byte of the COB-ID, and the node id from the low seven bits.
    private static string PdoName(uint cobId)
    {
        var isTransmit = (cobId & 0x80) != 0;
        var number = (cobId >> 8) - (isTransmit ? 0u : 1u);
        var nodeId = cobId & 0x7F;

        return $"{(isTransmit ? "Tx" : "Rx")}PDO{number}_node{nodeId}";
    }

    private static long RequiredParameter(ObjectDictionary od, int index, int subindex)
    {
        var variable = od.GetVariable(index, subindex)
            ?? throw new CanToolsException(
                $"The object dictionary has no entry 0x{index:X4}sub{subindex}, "
                + "which the PDO configuration requires.");

        // The DCF-configured value wins over the EDS default.
        var value = variable.Value ?? variable.Default
            ?? throw new CanToolsException(
                $"0x{index:X4}sub{subindex} ({variable.Name}) has neither a "
                + "ParameterValue nor a DefaultValue.");

        return (long)value.ToUInt64();
    }
}
