using CanTools.Formats;

namespace CanTools.Tests;

// ported from test_database.py::test_load_file_with_database_format
public class DatabaseLoaderTests
{
    [Fact]
    public void The_format_follows_the_file_extension()
    {
        Assert.NotEmpty(DatabaseLoader.LoadFile(TestFiles.Dbc("foobar.dbc")).Messages);
        Assert.NotEmpty(DatabaseLoader.LoadFile(TestFiles.Kcd("the_homer.kcd")).Messages);
        Assert.NotEmpty(DatabaseLoader.LoadFile(TestFiles.Sym("jopp-6.0.sym")).Messages);
    }

    [Fact]
    public void An_explicit_format_matching_the_content_loads()
    {
        Assert.NotEmpty(
            DatabaseLoader.LoadFile(TestFiles.Dbc("foobar.dbc"), DatabaseFormat.Dbc).Messages);
        Assert.NotEmpty(
            DatabaseLoader.LoadFile(TestFiles.Kcd("the_homer.kcd"), DatabaseFormat.Kcd).Messages);
        Assert.NotEmpty(
            DatabaseLoader.LoadFile(TestFiles.Sym("jopp-6.0.sym"), DatabaseFormat.Sym).Messages);
    }

    [Fact]
    public void A_mismatched_format_reports_why_it_failed()
    {
        var kcdOfDbc = Assert.Throws<UnsupportedDatabaseFormatException>(
            () => DatabaseLoader.LoadFile(TestFiles.Dbc("foobar.dbc"), DatabaseFormat.Kcd));

        // upstream reports expat's message here; ours is the ported XML wording
        Assert.Equal("KCD: \"syntax error: line 1, column 0\"", kcdOfDbc.Message);
        Assert.NotNull(kcdOfDbc.KcdError);
        Assert.Null(kcdOfDbc.DbcError);

        var dbcOfKcd = Assert.Throws<UnsupportedDatabaseFormatException>(
            () => DatabaseLoader.LoadFile(TestFiles.Kcd("the_homer.kcd"), DatabaseFormat.Dbc));

        Assert.Equal(
            "DBC: \"Invalid syntax at line 1, column 1: \">>!<<<!--\"\"",
            dbcOfKcd.Message);

        var symOfKcd = Assert.Throws<UnsupportedDatabaseFormatException>(
            () => DatabaseLoader.LoadFile(TestFiles.Kcd("the_homer.kcd"), DatabaseFormat.Sym));

        Assert.Equal("SYM: \"Only SYM version 6.0 is supported.\"", symOfKcd.Message);
    }

    [Fact]
    public void An_unknown_extension_probes_every_format()
    {
        // ported from test_database.py::test_invalid_kcd (explicit format)
        var invalidKcd = Assert.Throws<UnsupportedDatabaseFormatException>(
            () => DatabaseLoader.LoadString("<WrongRootElement/>", DatabaseFormat.Kcd));

        Assert.Equal(
            "KCD: \"Expected root element tag "
            + "{http://kayak.2codeornot2code.org/1.0}NetworkDefinition, "
            + "but got WrongRootElement.\"",
            invalidKcd.Message);

        // transparent format: all three attempts are reported
        var garbage = Assert.Throws<UnsupportedDatabaseFormatException>(
            () => DatabaseLoader.LoadString("not a database"));

        Assert.NotNull(garbage.DbcError);
        Assert.NotNull(garbage.KcdError);
        Assert.NotNull(garbage.SymError);
        Assert.StartsWith("DBC: \"", garbage.Message);
        Assert.Contains(", KCD: \"", garbage.Message);
        Assert.Contains(", SYM: \"", garbage.Message);
    }

    [Fact]
    public void Probing_detects_the_format_from_the_content()
    {
        var kcd = File.ReadAllText(TestFiles.Kcd("the_homer.kcd"));

        Assert.NotEmpty(DatabaseLoader.LoadString(kcd).Messages);
    }
}
