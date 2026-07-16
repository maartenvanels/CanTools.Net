using CanTools.Cli;

namespace CanTools.Tests.Cli;

internal static class CliRunner
{
    public static (int ExitCode, string Stdout, string Stderr) Run(string[] args, string stdin = "")
    {
        using var stdout = new StringWriter { NewLine = "\n" };
        using var stderr = new StringWriter { NewLine = "\n" };
        var exitCode = CliProgram.Run(args, new StringReader(stdin), stdout, stderr);

        return (exitCode, stdout.ToString(), stderr.ToString());
    }
}
