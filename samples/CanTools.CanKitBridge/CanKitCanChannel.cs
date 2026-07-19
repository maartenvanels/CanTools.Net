using CanKit.Abstractions.API.Can;
using CanTools.Transport;

namespace CanTools.CanKitBridge;

/// <summary>
/// An <see cref="ICanChannel"/> backed by a CanKit bus. Bridges CanTools.Net's
/// (id, payload) frames and CanKit's <c>CanFrame</c> via <see cref="FrameBridge"/>.
/// Classic 11-bit data frames only, matching FrameBridge's current scope.
/// </summary>
public sealed class CanKitCanChannel : ICanChannel
{
    // Poll interval for Receive: short enough that cancellation is honoured
    // promptly, long enough to avoid busy-spinning the virtual bus.
    private const int PollTimeoutMs = 100;

    private readonly ICanBus _bus;

    public CanKitCanChannel(ICanBus bus) => _bus = bus;

    public ValueTask SendAsync(CanFrame frame, CancellationToken cancellationToken = default)
    {
        _bus.Transmit(FrameBridge.ToCanKit(frame.Id, frame.Data));
        return ValueTask.CompletedTask;
    }

    public ValueTask<CanFrame> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var received = _bus.Receive(1, PollTimeoutMs).ToList();
            if (received.Count > 0)
            {
                var (id, payload) = FrameBridge.FromCanKit(received[0].CanFrame);
                return ValueTask.FromResult(new CanFrame(id, payload));
            }
        }
    }
}
