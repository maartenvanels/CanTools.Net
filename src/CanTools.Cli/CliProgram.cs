using System.Reflection;

namespace CanTools.Cli;

/// <summary>Subcommand dispatch of the cantools-net command line tool.</summary>
public static class CliProgram
{
    private const string Usage = "usage: cantools-net [--version] {convert,decode,dump,list} ...";

    public static int Run(string[] args, TextReader stdin, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
        {
            stderr.WriteLine(Usage);
            return 2;
        }

        if (args[0] == "--version")
        {
            stdout.WriteLine(Version);
            return 0;
        }

        try
        {
            switch (args[0])
            {
                case "convert":
                    ConvertCommand.Run(args[1..]);
                    return 0;
                case "decode":
                    DecodeCommand.Run(args[1..], stdin, stdout);
                    return 0;
                case "dump":
                    DumpCommand.Run(args[1..], stdout);
                    return 0;
                case "list":
                    ListCommand.Run(args[1..], stdout);
                    return 0;
                default:
                    stderr.WriteLine(Usage);
                    stderr.WriteLine($"cantools-net: error: unknown subcommand '{args[0]}'");
                    return 2;
            }
        }
        catch (Exception exception)
        {
            // like upstream, which wraps every subcommand in sys.exit('error: ' + str(e))
            stderr.WriteLine($"error: {exception.Message}");
            return 1;
        }
    }

    private static string Version
    {
        get
        {
            var informational = typeof(CliProgram).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            // drop the SourceLink commit suffix ("0.1.0+abcdef...")
            return informational?.Split('+')[0] ?? "unknown";
        }
    }
}
