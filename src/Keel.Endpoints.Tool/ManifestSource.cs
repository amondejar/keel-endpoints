using System.Diagnostics;

namespace Keel.Endpoints.Tool;

/// <summary>The two versions of one manifest. <c>null</c> content: the manifest does not exist on that side.</summary>
internal sealed record ManifestPair(string Path, string BaseLabel, string HeadLabel, string? Base, string? Head)
{
    public bool Added => Base is null && Head is not null;

    public bool Removed => Base is not null && Head is null;
}

/// <summary>Finds the manifests to compare: two files, or every manifest at two git refs.</summary>
internal static class ManifestSource
{
    private const string WorkingTree = "working tree";

    public static IReadOnlyList<ManifestPair> Resolve(DiffOptions options, string workingDirectory)
    {
        // Two files: compare them directly, no git needed.
        if (options.Head is { } head && IsFile(options.Base, workingDirectory) && IsFile(head, workingDirectory))
        {
            return [new ManifestPair(head, options.Base, head,
                File.ReadAllText(Path.GetFullPath(options.Base, workingDirectory)),
                File.ReadAllText(Path.GetFullPath(head, workingDirectory)))];
        }

        var root = Git(workingDirectory, "rev-parse", "--show-toplevel").Trim();
        Verify(root, options.Base);
        if (options.Head is not null) Verify(root, options.Head);

        var paths = options.Manifests.Count > 0
            ? options.Manifests.Select(m => RepositoryPath(root, workingDirectory, m)).ToList()
            : Discover(root, options.Base).Union(Discover(root, options.Head), StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();

        if (paths.Count == 0)
            throw new ToolException("No api.manifest.json found. Pass one with --manifest, or create it with ApiManifest.Verify (Keel.Endpoints.Testing).");

        return [.. paths.Select(path => new ManifestPair(
            path,
            options.Base,
            options.Head ?? WorkingTree,
            Read(root, options.Base, path),
            Read(root, options.Head, path)))];
    }

    private static bool IsFile(string value, string workingDirectory) =>
        value.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.GetFullPath(value, workingDirectory));

    private static void Verify(string root, string reference)
    {
        if (TryGit(root, out _, "rev-parse", "--verify", "--quiet", reference + "^{commit}")) return;
        throw new ToolException($"'{reference}' is neither a manifest file nor a git ref here. In CI, fetch it first (actions/checkout with fetch-depth: 0).");
    }

    /// <summary>Manifests tracked at <paramref name="reference"/>, or in the working tree (tracked and new files).</summary>
    private static IEnumerable<string> Discover(string root, string? reference)
    {
        var listing = reference is null
            ? Git(root, "ls-files", "--cached", "--others", "--exclude-standard")
            : Git(root, "ls-tree", "-r", "--name-only", reference);

        return listing.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.EndsWith("api.manifest.json", StringComparison.OrdinalIgnoreCase));
    }

    private static string? Read(string root, string? reference, string path)
    {
        if (reference is null)
        {
            var file = System.IO.Path.Combine(root, path);
            return File.Exists(file) ? File.ReadAllText(file) : null;
        }

        return TryGit(root, out var content, "show", $"{reference}:{path}") ? content : null;
    }

    private static string RepositoryPath(string root, string workingDirectory, string path) =>
        System.IO.Path.GetRelativePath(root, System.IO.Path.GetFullPath(path, workingDirectory)).Replace('\\', '/');

    private static string Git(string directory, params string[] arguments) =>
        TryGit(directory, out var output, arguments)
            ? output
            : throw new ToolException($"git {string.Join(' ', arguments)} failed: {output.Trim()}");

    private static bool TryGit(string directory, out string output, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new ToolException("could not start git.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new ToolException("git was not found on PATH. Compare two files instead: dotnet keel diff old.json new.json");
        }

        using (process)
        {
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            output = process.ExitCode == 0 ? stdout.Result : stderr;
            return process.ExitCode == 0;
        }
    }
}
