using CanTools.Formats;

namespace CanTools.Tests.Cli;

public class ConvertCommandTests
{
    // ported from test_command_line.py::test_convert; upstream converts DBC -> KCD -> DBC,
    // but the KCD writer is not ported, so this covers the KCD -> DBC leg and a DBC round
    // trip instead
    [Fact]
    public void Convert_kcd_to_dbc()
    {
        var outPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".dbc");

        try
        {
            var (exitCode, _, stderr) = CliRunner.Run(
                ["convert", TestFiles.Kcd("the_homer.kcd"), outPath]);

            Assert.Equal("", stderr);
            Assert.Equal(0, exitCode);

            var database = DatabaseLoader.LoadFile(outPath);
            Assert.Equal("1.23", database.Version);
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    [Fact]
    public void Convert_dbc_round_trip()
    {
        var outPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".dbc");

        try
        {
            var (exitCode, _, stderr) = CliRunner.Run(
                ["convert", TestFiles.Dbc("motohawk.dbc"), outPath]);

            Assert.Equal("", stderr);
            Assert.Equal(0, exitCode);

            var database = DatabaseLoader.LoadFile(outPath);
            Assert.Equal("1.0", database.Version);
        }
        finally
        {
            File.Delete(outPath);
        }
    }

    // ported from test_command_line.py::test_convert_bad_outfile
    [Fact]
    public void Convert_unsupported_output_format()
    {
        var (exitCode, _, stderr) = CliRunner.Run(
            ["convert", TestFiles.Dbc("motohawk.dbc"), "test_command_line_convert.foo"]);

        Assert.Equal(1, exitCode);
        Assert.Equal("error: Unsupported output database format 'foo'.\n", stderr);
    }
}
