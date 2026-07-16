namespace CanTools.Cli;

/// <summary>A subcommand usage or input error; the message is printed as "error: ...".</summary>
internal sealed class CommandLineException(string message) : Exception(message);
