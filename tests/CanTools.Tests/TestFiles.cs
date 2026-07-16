namespace CanTools.Tests;

internal static class TestFiles
{
    private static readonly string Root = FindRoot();

    private static string FindRoot()
    {
        var directory = AppContext.BaseDirectory;

        while (!Directory.Exists(Path.Combine(directory, "tests", "files")))
        {
            directory = Directory.GetParent(directory)?.FullName
                ?? throw new DirectoryNotFoundException("tests/files not found above " + AppContext.BaseDirectory);
        }

        return Path.Combine(directory, "tests", "files");
    }

    public static string Dbc(string name) => Path.Combine(Root, "dbc", name);

    public static string Kcd(string name) => Path.Combine(Root, "kcd", name);

    public static string Sym(string name) => Path.Combine(Root, "sym", name);

    public static string Eds(string name) => Path.Combine(Root, "eds", name);
}
