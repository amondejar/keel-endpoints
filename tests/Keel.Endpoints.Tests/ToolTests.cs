using System.Diagnostics;
using System.Text.Json;
using Keel.Endpoints.Tool;
using static Keel.Endpoints.Tests.Harness;

namespace Keel.Endpoints.Tests;

internal static class ToolTests
{
    private const string V1 = """
        { "format": 1,
          "endpoints": {
            "GET api/orders/{id}": { "operation": "OrdersGet", "authorization": "required", "path": { "id": "string" }, "responses": { "200": "Order" } },
            "DELETE api/orders/{id}": { "operation": "OrdersDelete", "authorization": "required", "policies": ["admin"], "path": { "id": "string" }, "responses": { "204": "" } }
          },
          "types": { "Order": { "id": "string", "total": "number" } } }
        """;

    // Order.total removed (breaking), admin policy dropped (security), GET api/health added (compatible).
    private const string V2 = """
        { "format": 1,
          "endpoints": {
            "GET api/orders/{id}": { "operation": "OrdersGet", "authorization": "required", "path": { "id": "string" }, "responses": { "200": "Order" } },
            "DELETE api/orders/{id}": { "operation": "OrdersDelete", "authorization": "required", "path": { "id": "string" }, "responses": { "204": "" } },
            "GET api/health": { "operation": "Health", "authorization": "anonymous", "responses": { "200": "string as text/plain" } }
          },
          "types": { "Order": { "id": "string" } } }
        """;

    public static void Register()
    {
        Test("Tool: diff of two files lists changes by kind; --fail-on decides the exit code", () =>
        {
            using var dir = new TempDirectory();
            File.WriteAllText(dir.File("old.json"), V1);
            File.WriteAllText(dir.File("new.json"), V2);

            var (code, output, _) = Run(dir.Path, "diff", "old.json", "new.json");
            Assert.Equal(Cli.Ok, code);
            Assert.True(output.Contains("1 breaking, 1 security, 1 compatible"));
            Assert.True(output.Contains("breaking   GET api/orders/{id}: response 200.total removed"));
            Assert.True(output.Contains("security   DELETE api/orders/{id}: no longer requires policy 'admin'"));

            Assert.Equal(Cli.ChangesFound, Run(dir.Path, "diff", "old.json", "new.json", "--fail-on", "breaking").Code);
            Assert.Equal(Cli.ChangesFound, Run(dir.Path, "diff", "old.json", "new.json", "--fail-on", "security").Code);
            Assert.Equal(Cli.Ok, Run(dir.Path, "diff", "old.json", "old.json", "--fail-on", "any").Code);
        });

        Test("Tool: Markdown report warns on breaking changes and folds the compatible ones", () =>
        {
            using var dir = new TempDirectory();
            File.WriteAllText(dir.File("old.json"), V1);
            File.WriteAllText(dir.File("new.json"), V2);

            var (_, md, _) = Run(dir.Path, "diff", "old.json", "new.json", "--format", "markdown");
            Assert.True(md.StartsWith("## API changes", StringComparison.Ordinal));
            Assert.True(md.Contains("> [!WARNING]\n> 1 breaking change"));
            Assert.True(md.Contains("| breaking | `GET api/orders/{id}` | response 200.total removed |"));
            Assert.True(md.Contains("<details><summary>1 compatible change</summary>"));

            var (_, same, _) = Run(dir.Path, "diff", "old.json", "old.json", "--format", "markdown");
            Assert.True(same.Contains("No API changes."));
        });

        Test("Tool: diff between git refs finds the manifests and reads each side from git", () =>
        {
            using var dir = new TempDirectory();
            Git(dir.Path, "init", "-q", "-b", "main");
            Directory.CreateDirectory(dir.File("api"));
            File.WriteAllText(dir.File("api/api.manifest.json"), V1);
            Git(dir.Path, "add", ".");
            Git(dir.Path, "commit", "-q", "-m", "v1");
            Git(dir.Path, "tag", "v1");

            // Working tree against v1, before committing.
            File.WriteAllText(dir.File("api/api.manifest.json"), V2);
            var (code, output, _) = Run(dir.Path, "diff", "v1", "--fail-on", "breaking");
            Assert.Equal(Cli.ChangesFound, code);
            Assert.True(output.Contains("api/api.manifest.json (v1 -> working tree): 1 breaking"));

            // Two refs, as JSON, run from a subfolder.
            Git(dir.Path, "commit", "-q", "-am", "v2");
            var (_, json, _) = Run(dir.File("api"), "diff", "v1", "HEAD", "--format", "json");
            using var doc = JsonDocument.Parse(json);
            var manifest = doc.RootElement.GetProperty("manifests")[0];
            Assert.Equal("api/api.manifest.json", manifest.GetProperty("path").GetString());
            Assert.Equal(1, manifest.GetProperty("breaking").GetInt32());
            Assert.Equal(3, manifest.GetProperty("changes").GetArrayLength());

            // A manifest new at HEAD reads as all additions.
            File.WriteAllText(dir.File("second.api.manifest.json"), V1);
            var (_, withNew, _) = Run(dir.Path, "diff", "HEAD");
            Assert.True(withNew.Contains("second.api.manifest.json (HEAD -> working tree): new manifest. 0 breaking, 0 security, 2 compatible."));
        });

        Test("Tool: unknown refs and options are reported with exit code 2", () =>
        {
            using var dir = new TempDirectory();
            Git(dir.Path, "init", "-q");
            var (code, _, error) = Run(dir.Path, "diff", "v9.9.9");
            Assert.Equal(Cli.UsageError, code);
            Assert.True(error.Contains("'v9.9.9' is neither a manifest file nor a git ref"));

            Assert.Equal(Cli.UsageError, Run(dir.Path, "diff", "a", "--format", "xml").Code);
            Assert.Equal(Cli.UsageError, Run(dir.Path, "publish").Code);
            Assert.Equal(Cli.Ok, Run(dir.Path, "--help").Code);
        });
    }

    private static (int Code, string Output, string Error) Run(string directory, params string[] args)
    {
        var output = new StringWriter { NewLine = "\n" };
        var error = new StringWriter { NewLine = "\n" };
        var code = Cli.Run(args, output, error, directory);
        return (code, output.ToString(), error.ToString());
    }

    private static void Git(string directory, params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var a in args) start.ArgumentList.Add(a);
        start.Environment["GIT_AUTHOR_NAME"] = start.Environment["GIT_COMMITTER_NAME"] = "test";
        start.Environment["GIT_AUTHOR_EMAIL"] = start.Environment["GIT_COMMITTER_EMAIL"] = "test@example.com";
        using var process = Process.Start(start)!;
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new AssertionException($"git {string.Join(' ', args)}: {stderr}");
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("keel-tool-").FullName;

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
