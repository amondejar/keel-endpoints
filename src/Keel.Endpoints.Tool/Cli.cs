using System.Reflection;
using Keel.Endpoints.Testing;

namespace Keel.Endpoints.Tool;

/// <summary>Parses the command line and runs a command. Returns the process exit code.</summary>
internal static class Cli
{
    public const int Ok = 0;
    public const int ChangesFound = 1;
    public const int UsageError = 2;

    private const string Usage = """
        Usage: dotnet keel diff <base> [<head>] [options]

        Compares api.manifest.json between <base> and <head> and lists what changed for clients.
        <base> and <head> are git refs (a tag, a branch, a commit) or two manifest files.
        <head> defaults to the working tree.

        Options:
          --manifest <path>   A manifest to compare (repeatable). Default: every *api.manifest.json in the repository.
          --format <format>   text (default), markdown or json.
          --output <file>     Write the report to a file instead of the console.
          --fail-on <level>   Exit with code 1 on: breaking, security (breaking or security), any, or none (default).

        Examples:
          dotnet keel diff v1.0.0                    changes since v1.0.0, in the working tree
          dotnet keel diff main HEAD --fail-on breaking
          dotnet keel diff old.json new.json --format markdown
        """;

    public static int Run(string[] args, TextWriter output, TextWriter error, string workingDirectory)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            output.WriteLine(Usage);
            return args.Length == 0 ? UsageError : Ok;
        }

        if (args[0] is "--version")
        {
            output.WriteLine(typeof(Cli).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown");
            return Ok;
        }

        if (args[0] != "diff")
        {
            error.WriteLine($"Unknown command '{args[0]}'.\n");
            error.WriteLine(Usage);
            return UsageError;
        }

        try
        {
            var options = DiffOptions.Parse(args[1..]);
            return Diff(options, output, workingDirectory);
        }
        catch (UsageException e)
        {
            error.WriteLine(e.Message + "\n");
            error.WriteLine(Usage);
            return UsageError;
        }
        catch (ToolException e)
        {
            error.WriteLine("error: " + e.Message);
            return UsageError;
        }
    }

    private static int Diff(DiffOptions options, TextWriter output, string workingDirectory)
    {
        var pairs = ManifestSource.Resolve(options, workingDirectory);
        var results = pairs.Select(p => new ManifestDiff(p, Compare(p))).ToList();

        var report = options.Format switch
        {
            ReportFormat.Markdown => Reports.Markdown(results),
            ReportFormat.Json => Reports.Json(results),
            _ => Reports.Text(results),
        };

        if (options.Output is { } file)
        {
            File.WriteAllText(Path.GetFullPath(file, workingDirectory), report);
        }
        else
        {
            output.Write(report);
        }

        var changes = results.SelectMany(r => r.Changes).ToList();
        var fail = options.FailOn switch
        {
            FailOn.Breaking => changes.Any(c => c.Kind == ApiChangeKind.Breaking) || results.Any(r => r.Pair.Removed),
            FailOn.Security => changes.Any(c => c.Kind is ApiChangeKind.Breaking or ApiChangeKind.Security) || results.Any(r => r.Pair.Removed),
            FailOn.Any => changes.Count > 0 || results.Any(r => r.Pair.Added || r.Pair.Removed),
            _ => false,
        };
        return fail ? ChangesFound : Ok;
    }

    private static IReadOnlyList<ApiChange> Compare(ManifestPair pair)
    {
        // A manifest that appears is all additions; one that disappears removes every endpoint.
        var empty = new ApiManifest();
        try
        {
            var before = pair.Base is null ? empty : ApiManifest.Parse(pair.Base);
            var after = pair.Head is null ? empty : ApiManifest.Parse(pair.Head);
            return ApiManifest.Compare(before, after);
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException)
        {
            throw new ToolException($"{pair.Path}: {e.Message}");
        }
    }
}

internal enum ReportFormat { Text, Markdown, Json }

internal enum FailOn { None, Breaking, Security, Any }

internal sealed class DiffOptions
{
    public required string Base { get; init; }

    public string? Head { get; init; }

    public IReadOnlyList<string> Manifests { get; init; } = [];

    public ReportFormat Format { get; init; }

    public string? Output { get; init; }

    public FailOn FailOn { get; init; }

    public static DiffOptions Parse(string[] args)
    {
        var positional = new List<string>();
        var manifests = new List<string>();
        var format = ReportFormat.Text;
        string? output = null;
        var failOn = FailOn.None;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            string Value() => i + 1 < args.Length ? args[++i] : throw new UsageException($"{arg} needs a value.");

            switch (arg)
            {
                case "--manifest":
                    manifests.Add(Value());
                    break;
                case "--format":
                    format = Value() switch
                    {
                        "text" => ReportFormat.Text,
                        "markdown" or "md" => ReportFormat.Markdown,
                        "json" => ReportFormat.Json,
                        var other => throw new UsageException($"Unknown format '{other}': use text, markdown or json."),
                    };
                    break;
                case "--output" or "-o":
                    output = Value();
                    break;
                case "--fail-on":
                    failOn = Value() switch
                    {
                        "none" => FailOn.None,
                        "breaking" => FailOn.Breaking,
                        "security" => FailOn.Security,
                        "any" => FailOn.Any,
                        var other => throw new UsageException($"Unknown --fail-on level '{other}': use breaking, security, any or none."),
                    };
                    break;
                default:
                    if (arg.StartsWith("--", StringComparison.Ordinal)) throw new UsageException($"Unknown option '{arg}'.");
                    positional.Add(arg);
                    break;
            }
        }

        if (positional.Count is 0 or > 2) throw new UsageException("diff takes a <base> and an optional <head>.");
        return new DiffOptions
        {
            Base = positional[0],
            Head = positional.Count == 2 ? positional[1] : null,
            Manifests = manifests,
            Format = format,
            Output = output,
            FailOn = failOn,
        };
    }
}

internal sealed class UsageException(string message) : Exception(message);

internal sealed class ToolException(string message) : Exception(message);
