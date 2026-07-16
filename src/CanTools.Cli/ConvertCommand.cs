using System.Text;
using CanTools.Formats;
using CanTools.Formats.Dbc;
using CanTools.Model;

namespace CanTools.Cli;

/// <summary>The convert subcommand: read a database file and write it in another format.</summary>
internal static class ConvertCommand
{
    public static void Run(IReadOnlyList<string> args)
    {
        string? encodingName = null;
        var prune = false;
        var noStrict = false;
        var positionals = new List<string>();
        var expanded = Args.Expand(args);

        for (var i = 0; i < expanded.Count; i++)
        {
            switch (expanded[i])
            {
                case "-e" or "--encoding":
                    encodingName = Args.Value(expanded, ref i, expanded[i]);
                    break;
                case "--prune":
                    prune = true;
                    break;
                case "--no-strict":
                    noStrict = true;
                    break;
                case var option when option.StartsWith('-'):
                    throw Args.Unknown(option);
                default:
                    positionals.Add(expanded[i]);
                    break;
            }
        }

        if (positionals.Count != 2)
        {
            throw new CommandLineException("the following arguments are required: infile, outfile");
        }

        var encoding = encodingName is null ? null : TextEncodings.Resolve(encodingName);
        var database = DatabaseLoader.LoadFile(positionals[0],
                                               encoding: encoding,
                                               strict: !noStrict,
                                               pruneChoices: prune);

        DumpFile(database, positionals[1], encoding);
    }

    private static void DumpFile(Database database, string path, Encoding? encoding)
    {
        var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();

        // upstream also writes .kcd and .sym; those writers are not ported yet
        if (extension != "dbc")
        {
            throw new CommandLineException($"Unsupported output database format '{extension}'.");
        }

        File.WriteAllText(path, DbcWriter.Dump(database), encoding ?? DbcReader.DefaultEncoding);
    }
}
