using CanTools.Transport;

namespace CanTools.Tests.Transport;

/// <summary>
/// A scripted <see cref="ICanChannel"/> for driving the SDO client in tests.
/// Enqueue the server's responses in order; the client reads them back as it
/// expects them. Assert on <see cref="Sent"/> to verify the requests it produced.
/// A silent bus (no more enqueued frames) makes ReceiveAsync wait for cancellation.
/// </summary>
internal sealed class InMemoryCanChannel : ICanChannel
{
    private readonly Queue<CanFrame> _incoming = new();
    private readonly List<CanFrame> _sent = [];

    public IReadOnlyList<CanFrame> Sent => _sent;

    public void Enqueue(params CanFrame[] frames)
    {
        foreach (var frame in frames)
        {
            _incoming.Enqueue(frame);
        }
    }

    public ValueTask SendAsync(CanFrame frame, CancellationToken cancellationToken = default)
    {
        _sent.Add(frame);
        return ValueTask.CompletedTask;
    }

    public async ValueTask<CanFrame> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        if (_incoming.Count > 0)
        {
            return _incoming.Dequeue();
        }

        var completion = new TaskCompletionSource<CanFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = cancellationToken.Register(
            () => completion.TrySetCanceled(cancellationToken));

        return await completion.Task;
    }
}
