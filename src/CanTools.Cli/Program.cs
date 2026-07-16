using CanTools.Cli;

// LF-only output keeps the format identical to upstream cantools on every platform.
Console.Out.NewLine = "\n";
Console.Error.NewLine = "\n";

return CliProgram.Run(args, Console.In, Console.Out, Console.Error);
