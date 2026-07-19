using CanTools.Formats.Eds;

namespace CanTools.Tests;

public class IniDocumentTests
{
    private const string Sample =
        "; a leading comment\n" +
        "[DeviceInfo]\n" +
        "VendorName=Acme\n" +
        "\n" +
        "[1000]\n" +
        "ParameterName=Device type   ; inline note\n" +
        "DataType=0x0007\n" +
        "\n" +
        "[1a00sub1]\n" +
        "ParameterName=Mapping\n";

    [Fact]
    public void Round_trips_an_unchanged_document_verbatim()
    {
        var document = IniDocument.Parse(Sample);

        Assert.Equal(Sample, document.ToString());
    }

    [Fact]
    public void Preserves_crlf_line_endings()
    {
        var crlf = Sample.Replace("\n", "\r\n");

        Assert.Equal(crlf, IniDocument.Parse(crlf).ToString());
    }

    [Fact]
    public void Upsert_updates_an_existing_key_in_place()
    {
        var document = IniDocument.Parse(Sample);

        document.UpsertInSection("DeviceInfo", "VendorName", "NewCo");

        Assert.Contains("VendorName=NewCo", document.ToString());
        Assert.DoesNotContain("VendorName=Acme", document.ToString());
        Assert.Contains("; a leading comment", document.ToString());   // untouched
    }

    [Fact]
    public void Upsert_appends_a_missing_key_within_the_section()
    {
        var document = IniDocument.Parse(Sample);

        document.UpsertInSection("DeviceInfo", "ProductName", "Widget");

        // added inside [DeviceInfo], before its trailing blank line
        var text = document.ToString();
        Assert.Contains("VendorName=Acme\nProductName=Widget\n\n[1000]", text);
    }

    [Fact]
    public void Upsert_creates_a_missing_section_after_device_info()
    {
        var document = IniDocument.Parse(Sample);

        document.UpsertInSection("DeviceComissioning", "NodeID", "0x0A");

        var text = document.ToString();
        Assert.Contains("[DeviceComissioning]\nNodeID=0x0A", text);
        // it lands right after the [DeviceInfo] block, not before it
        Assert.True(text.IndexOf("[DeviceInfo]") < text.IndexOf("[DeviceComissioning]"));
    }

    [Fact]
    public void TryUpsertObject_matches_a_plain_section_case_insensitively()
    {
        var document = IniDocument.Parse(Sample);

        Assert.True(document.TryUpsertObject(0x1000, null, "ParameterValue", "5"));
        Assert.Contains("ParameterValue=5", document.ToString());
    }

    [Fact]
    public void TryUpsertObject_matches_a_subindex_section()
    {
        var document = IniDocument.Parse(Sample);

        // [1a00sub1] — lowercase index, matched by parsed number
        Assert.True(document.TryUpsertObject(0x1A00, 1, "ParameterValue", "7"));
        Assert.Contains("ParameterValue=7", document.ToString());
    }

    [Fact]
    public void TryUpsertObject_returns_false_for_a_missing_section()
    {
        var document = IniDocument.Parse(Sample);

        Assert.False(document.TryUpsertObject(0x3000, null, "ParameterValue", "1"));
    }

    [Fact]
    public void TryUpsertObject_does_not_match_a_plain_section_when_a_subindex_is_wanted()
    {
        var document = IniDocument.Parse(Sample);

        Assert.False(document.TryUpsertObject(0x1000, 1, "ParameterValue", "1"));
    }
}
