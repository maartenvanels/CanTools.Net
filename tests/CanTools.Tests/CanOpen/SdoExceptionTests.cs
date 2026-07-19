using CanTools.CanOpen;

namespace CanTools.Tests.CanOpen;

public class SdoExceptionTests
{
    [Fact]
    public void An_abort_exception_carries_the_code_and_target()
    {
        var ex = new SdoAbortException(0x1018, 1, SdoAbortCode.ObjectDoesNotExist);

        Assert.Equal(0x1018, ex.Index);
        Assert.Equal(1, ex.Subindex);
        Assert.Equal(SdoAbortCode.ObjectDoesNotExist, ex.Code);
        Assert.IsAssignableFrom<CanToolsException>(ex);
    }

    [Fact]
    public void Options_default_to_a_half_second_timeout_without_block_transfer()
    {
        var options = new SdoClientOptions();

        Assert.Equal(TimeSpan.FromMilliseconds(500), options.Timeout);
        Assert.False(options.EnableBlockTransfer);
    }
}
