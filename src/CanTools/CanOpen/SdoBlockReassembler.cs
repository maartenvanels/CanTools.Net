namespace CanTools.CanOpen;

/// <summary>
/// Accumulates the segments of an SDO block transfer. A round of up to 127
/// sequence-numbered segments is buffered; only an acknowledgement commits them,
/// so a retransmitted round overwrites instead of duplicating. Shared by the
/// passive log interpreter and the active SDO client.
/// </summary>
internal sealed class SdoBlockReassembler
{
    private readonly byte[]?[] _round = new byte[128][];
    private int _lastSequence;

    /// <summary>The committed payload so far.</summary>
    public List<byte> Data { get; } = [];

    /// <summary>Buffers one raw block data frame (sequence number in byte 0, bit 7 = last).</summary>
    public void AddSegment(byte[] frame)
    {
        var sequence = frame[0] & 0x7F;

        if (sequence == 0)
        {
            return;
        }

        _round[sequence] = frame.Length > 1 ? frame[1..Math.Min(8, frame.Length)] : [];

        if ((frame[0] & 0x80) != 0)
        {
            _lastSequence = sequence;
        }
    }

    /// <summary>
    /// Commits sequence numbers 1..<paramref name="acknowledged"/> of the round.
    /// Returns true when the last segment of the transfer has been committed.
    /// </summary>
    public bool Acknowledge(int acknowledged)
    {
        for (var sequence = 1; sequence <= acknowledged && sequence < _round.Length; sequence++)
        {
            if (_round[sequence] is { } segment)
            {
                Data.AddRange(segment);
            }
        }

        var finished = _lastSequence != 0 && acknowledged >= _lastSequence;
        Array.Clear(_round);
        _lastSequence = 0;

        return finished;
    }

    /// <summary>Removes the padding bytes announced by the end frame.</summary>
    public void TrimTail(int count)
    {
        if (count > 0 && Data.Count >= count)
        {
            Data.RemoveRange(Data.Count - count, count);
        }
    }
}
