using CanTools.CanOpen;

namespace CanTools.Tests.CanOpen;

public class SdoValueCodecTests
{
    [Fact]
    public void It_decodes_an_unsigned32()
    {
        var value = SdoValueCodec.Decode([0x2A, 0x00, 0x00, 0x00], CanOpenDataType.Unsigned32);

        Assert.Equal((OdValue)42UL, value);
    }

    [Fact]
    public void It_decodes_a_signed16()
    {
        var value = SdoValueCodec.Decode([0xFF, 0xFF], CanOpenDataType.Integer16);

        Assert.Equal((OdValue)(-1L), value);
    }

    [Fact]
    public void It_decodes_a_real32()
    {
        var value = SdoValueCodec.Decode(BitConverter.GetBytes(1.5f), CanOpenDataType.Real32);

        Assert.Equal(1.5, value.ToDouble());
    }

    [Fact]
    public void It_decodes_a_visible_string()
    {
        var value = SdoValueCodec.Decode("hi"u8.ToArray(), CanOpenDataType.VisibleString);

        Assert.Equal("hi", value.Text);
    }

    [Fact]
    public void It_round_trips_an_unsigned32()
    {
        var encoded = SdoValueCodec.Encode((OdValue)42UL, CanOpenDataType.Unsigned32);

        Assert.Equal(new byte[] { 0x2A, 0x00, 0x00, 0x00 }, encoded);
    }

    [Fact]
    public void It_round_trips_a_negative_signed16()
    {
        var encoded = SdoValueCodec.Encode((OdValue)(-2L), CanOpenDataType.Integer16);

        Assert.Equal(new byte[] { 0xFE, 0xFF }, encoded);
        Assert.Equal((OdValue)(-2L), SdoValueCodec.Decode(encoded, CanOpenDataType.Integer16));
    }
}
