namespace CanTools.Cli;

/// <summary>Helpers shared by the hand-rolled option parsing of the subcommands.</summary>
internal static class Args
{
    /// <summary>Splits "--name=value" tokens so the parsing loops only see separate tokens.</summary>
    public static List<string> Expand(IReadOnlyList<string> args)
    {
        var result = new List<string>(args.Count);

        foreach (var arg in args)
        {
            var separator = arg.IndexOf('=');

            if (arg.StartsWith("--", StringComparison.Ordinal) && separator > 2)
            {
                result.Add(arg[..separator]);
                result.Add(arg[(separator + 1)..]);
            }
            else
            {
                result.Add(arg);
            }
        }

        return result;
    }

    public static string Value(IReadOnlyList<string> args, ref int index, string option)
    {
        if (index + 1 >= args.Count)
        {
            throw new CommandLineException($"argument {option}: expected a value.");
        }

        return args[++index];
    }

    public static CommandLineException Unknown(string argument) =>
        new($"unrecognized argument: {argument}");
}
