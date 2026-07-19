namespace CanTools.Transport;

/// <summary>
/// The single seam between the codec layer and a CAN bus. Implementations bridge
/// to concrete hardware/driver libraries; the core ships none, so it stays
/// dependency-free. Consumers impose their own timeouts via the cancellation token.
/// </summary>
public interface ICanChannel
{
    /// <summary>Transmits one frame.</summary>
    ValueTask SendAsync(CanFrame frame, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the next received frame, awaiting one if none is available. Honours
    /// the cancellation token so callers can bound the wait with a timeout.
    /// </summary>
    ValueTask<CanFrame> ReceiveAsync(CancellationToken cancellationToken = default);
}
