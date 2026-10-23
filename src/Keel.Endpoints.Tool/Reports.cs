using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Keel.Endpoints.Testing;

namespace Keel.Endpoints.Tool;

internal sealed record ManifestDiff(ManifestPair Pair, IReadOnlyList<ApiChange> Changes)
{
    public int Count(ApiChangeKind kind) => Changes.Count(c => c.Kind == kind);
}

/// <summary>The three report formats. Changes arrive sorted: breaking, security, compatible.</summary>
internal static class Reports
{
    public static string Text(IReadOnlyList<ManifestDiff> results)
    {
        var text = new StringBuilder();
        foreach (var r in results)
        {
            text.Append($"{r.Pair.Path} ({r.Pair.BaseLabel} -> {r.Pair.HeadLabel}): ");
            if (r.Pair.Added) text.Append("new manifest. ");
            if (r.Pair.Removed) text.Append("manifest removed. ");
            text.Append(r.Changes.Count == 0 ? "no API changes.\n" : Summary(r) + "\n");
            foreach (var change in r.Changes) text.Append("  ").Append(change).Append('\n');
        }

        return text.ToString();
    }

    public static string Markdown(IReadOnlyList<ManifestDiff> results)
    {
        var md = new StringBuilder();
        var total = results.Sum(r => r.Changes.Count);
        var breaking = results.Sum(r => r.Count(ApiChangeKind.Breaking));
        md.Append("## API changes\n\n");
        if (total == 0)
        {
            md.Append("No API changes.\n");
            return md.ToString();
        }

        if (breaking > 0) md.Append($"> [!WARNING]\n> {breaking} breaking {(breaking == 1 ? "change" : "changes")}: existing clients can stop working.\n\n");

        foreach (var r in results)
        {
            md.Append($"`{r.Pair.Path}` ({Code(r.Pair.BaseLabel)} → {Code(r.Pair.HeadLabel)}): ");
            if (r.Pair.Added) md.Append("new manifest. ");
            if (r.Pair.Removed) md.Append("manifest removed. ");
            md.Append(r.Changes.Count == 0 ? "no API changes.\n\n" : Summary(r, bold: true) + "\n\n");

            var important = r.Changes.Where(c => c.Kind != ApiChangeKind.Compatible).ToList();
            var compatible = r.Changes.Where(c => c.Kind == ApiChangeKind.Compatible).ToList();
            if (important.Count > 0) Table(md, important);
            if (compatible.Count > 0)
            {
                md.Append($"<details><summary>{compatible.Count} compatible {(compatible.Count == 1 ? "change" : "changes")}</summary>\n\n");
                Table(md, compatible);
                md.Append("</details>\n\n");
            }
        }

        return md.ToString();
    }

    public static string Json(IReadOnlyList<ManifestDiff> results)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteStartArray("manifests");
            foreach (var r in results)
            {
                w.WriteStartObject();
                w.WriteString("path", r.Pair.Path);
                w.WriteString("base", r.Pair.BaseLabel);
                w.WriteString("head", r.Pair.HeadLabel);
                w.WriteString("status", r.Pair.Added ? "added" : r.Pair.Removed ? "removed" : "modified");
                w.WriteNumber("breaking", r.Count(ApiChangeKind.Breaking));
                w.WriteNumber("security", r.Count(ApiChangeKind.Security));
                w.WriteNumber("compatible", r.Count(ApiChangeKind.Compatible));
                w.WriteStartArray("changes");
                foreach (var c in r.Changes)
                {
                    w.WriteStartObject();
                    w.WriteString("kind", c.Kind.ToString().ToLowerInvariant());
                    w.WriteString("endpoint", c.Endpoint);
                    w.WriteString("description", c.Description);
                    w.WriteEndObject();
                }

                w.WriteEndArray();
                w.WriteEndObject();
            }

            w.WriteEndArray();
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    private static string Summary(ManifestDiff r, bool bold = false)
    {
        var breaking = $"{r.Count(ApiChangeKind.Breaking)} breaking";
        if (bold && r.Count(ApiChangeKind.Breaking) > 0) breaking = $"**{breaking}**";
        return $"{breaking}, {r.Count(ApiChangeKind.Security)} security, {r.Count(ApiChangeKind.Compatible)} compatible.";
    }

    private static void Table(StringBuilder md, IEnumerable<ApiChange> changes)
    {
        md.Append("| Kind | Endpoint | Change |\n| --- | --- | --- |\n");
        foreach (var c in changes)
            md.Append($"| {c.Kind.ToString().ToLowerInvariant()} | `{Cell(c.Endpoint)}` | {Cell(c.Description)} |\n");
        md.Append('\n');
    }

    private static string Code(string label) => label.Length == 40 && label.All(Uri.IsHexDigit) ? $"`{label[..7]}`" : $"`{label}`";

    private static string Cell(string text) => text.Replace("|", "\\|").Replace("\n", " ");
}
