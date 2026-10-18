namespace VsrSwitcher.Cli;

internal sealed class UsageException(string message) : Exception(message);

/// <summary>Global switches, stripped out of the argument list before command dispatch.</summary>
internal sealed class Options
{
    internal string? Adapter { get; private set; }
    internal bool Json { get; private set; }
    internal bool DryRun { get; private set; }
    internal bool Elevate { get; private set; }
    internal bool Help { get; private set; }
    internal bool Version { get; private set; }

    /// <summary>
    /// Set by <c>--elevate</c> on the child process: console output is redirected here so the
    /// parent can print it after the elevated run finishes in its own, invisible console.
    /// </summary>
    internal string? OutputFile { get; private set; }

    internal static Options Parse(IEnumerable<string> args, out List<string> positional)
    {
        var options = new Options();
        positional = [];

        using var enumerator = args.GetEnumerator();
        while (enumerator.MoveNext())
        {
            var arg = enumerator.Current;
            switch (arg)
            {
                case "--adapter" or "-a":
                    options.Adapter = Next(enumerator, arg);
                    break;
                case "--output-file":
                    options.OutputFile = Next(enumerator, arg);
                    break;
                case "--json":
                    options.Json = true;
                    break;
                case "--dry-run" or "-n":
                    options.DryRun = true;
                    break;
                case "--elevate":
                    options.Elevate = true;
                    break;
                case "--help" or "-h" or "-?" or "/?":
                    options.Help = true;
                    break;
                case "--version" or "-v":
                    options.Version = true;
                    break;
                default:
                    if (arg.StartsWith('-') && arg.Length > 1)
                    {
                        throw new UsageException($"unknown option '{arg}'");
                    }

                    positional.Add(arg);
                    break;
            }
        }

        return options;
    }

    private static string Next(IEnumerator<string> enumerator, string option) =>
        enumerator.MoveNext()
            ? enumerator.Current
            : throw new UsageException($"option '{option}' needs a value");
}
