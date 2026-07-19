using CanTools.Transport;

namespace CanTools.Tests.Transport;

public class InMemoryCanChannelTests
{
    [Fact]
    public async Task It_returns_enqueued_frames_in_order()
    {
        var channel = new InMemoryCanChannel();
        channel.Enqueue(CanFrame.Classic(0x581, [1]), CanFrame.Classic(0x581, [2]));

        Assert.Equal(1, (await channel.ReceiveAsync()).Data[0]);
        Assert.Equal(2, (await channel.ReceiveAsync()).Data[0]);
    }

    [Fact]
    public async Task It_records_sent_frames()
    {
        var channel = new InMemoryCanChannel();
        await channel.SendAsync(CanFrame.Classic(0x601, [0x40]));

        Assert.Single(channel.Sent);
        Assert.Equal(0x601u, channel.Sent[0].Id);
    }

    [Fact]
    public async Task Receive_on_a_silent_bus_honours_cancellation()
    {
        var channel = new InMemoryCanChannel();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await channel.ReceiveAsync(cts.Token));
    }
}
