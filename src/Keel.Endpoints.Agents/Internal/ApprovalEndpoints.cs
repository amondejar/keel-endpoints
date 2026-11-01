using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Keel.Endpoints.Agents.Internal;

/// <summary>
/// The approval page (<c>GET {pattern}/{id}</c>, HTML, or JSON with <c>Accept: application/json</c>) and the decision
/// (<c>POST {pattern}/{id}</c> with a JSON body <c>{"approve": true}</c>). A JSON body cannot be sent cross-site without
/// a CORS preflight, so a forged form on another site cannot approve; the page cannot be framed (clickjacking), keeps
/// the link out of referrers, and is not cached.
/// </summary>
internal static class ApprovalEndpoints
{
    /// <summary>
    /// Maps the pages in a group, returned so the app can add conventions (rate limiting, authorization). The approver
    /// rules (policy, four eyes) are checked here either way.
    /// </summary>
    public static RouteGroupBuilder Map(IEndpointRouteBuilder app, string pattern)
    {
        var group = app.MapGroup("/" + pattern.Trim('/'));
        group.MapMethods("/{id}", [HttpMethods.Get], (RequestDelegate)ShowAsync).ExcludeFromDescription().WithDisplayName("Agent approval page");
        group.MapMethods("/{id}", [HttpMethods.Post], (RequestDelegate)DecideAsync).ExcludeFromDescription().WithDisplayName("Agent approval decision");
        return group;
    }

    private static async Task ShowAsync(HttpContext context)
    {
        Secure(context.Response);
        var service = context.RequestServices.GetRequiredService<ApprovalService>();
        var texts = context.RequestServices.GetRequiredService<IOptions<KeelAgentsOptions>>().Value.ApprovalTexts;
        var id = context.Request.RouteValues["id"] as string ?? string.Empty;
        var approval = await service.Store.GetAsync(id, context.RequestAborted);
        if (approval is null)
        {
            await Page(context, StatusCodes.Status404NotFound, texts.Title, $"<p>{Html(texts.NotFound)}</p>", null);
            return;
        }

        var (status, error, _) = await service.CheckApproverAsync(approval, context.User);
        if (error is not null)
        {
            if (status == StatusCodes.Status401Unauthorized && await CanChallenge(context))
            {
                await context.ChallengeAsync();
                return;
            }

            await Page(context, status, texts.Title, $"<p>{Html(texts.NotAllowed)}</p><p class=\"detail\">{Html(error)}</p>", null);
            return;
        }

        if (Wants(context.Request, "application/json"))
        {
            await Json(context, StatusCodes.Status200OK, approval, null);
            return;
        }

        var expired = approval.Expires <= context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
        var state = expired ? texts.Expired : Label(texts, ApprovalService.Describe(approval.Status));
        var body = new StringBuilder()
            .Append("<p class=\"question\">").Append(Html(texts.Question)).Append("</p>")
            .Append("<p class=\"message\">").Append(Html(approval.Message)).Append("</p>")
            .Append("<dl>")
            .Append("<dt>").Append(Html(texts.Tool)).Append("</dt><dd><code>").Append(Html(approval.Tool)).Append("</code></dd>")
            .Append("<dt>").Append(Html(texts.AskedBy)).Append("</dt><dd>").Append(Html(approval.RequestedBy ?? texts.Anonymous)).Append("</dd>")
            .Append("<dt>").Append(Html(texts.Status)).Append("</dt><dd id=\"status\">").Append(Html(state)).Append("</dd>")
            .Append("<dt>").Append(Html(texts.Approvals)).Append("</dt><dd id=\"count\">")
            .Append(Html(string.Format(CultureInfo.InvariantCulture, texts.Count, approval.ApprovedBy.Count, approval.RequiredApprovals))).Append("</dd>")
            .Append("<dt>").Append(Html(texts.Expires)).Append("</dt><dd>").Append(approval.Expires.ToString("u", CultureInfo.InvariantCulture)).Append("</dd>")
            .Append("</dl>");
        if (!expired && approval.Status == AgentApprovalStatus.Pending)
        {
            body.Append("<div class=\"actions\"><button type=\"button\" data-approve=\"true\">").Append(Html(texts.Allow)).Append("</button> ")
                .Append("<button type=\"button\" data-approve=\"false\" class=\"deny\">").Append(Html(texts.Deny)).Append("</button></div>");
        }

        body.Append("<p id=\"result\" role=\"status\"></p>");
        await Page(context, StatusCodes.Status200OK, texts.Title, body.ToString(), Script(texts));
    }

    private static async Task DecideAsync(HttpContext context)
    {
        Secure(context.Response);
        if (!context.Request.HasJsonContentType())
        {
            await Message(context, StatusCodes.Status415UnsupportedMediaType, "Send {\"approve\": true} or {\"approve\": false} as application/json.");
            return;
        }

        bool approve;
        try
        {
            using var document = await JsonDocument.ParseAsync(context.Request.Body, default, context.RequestAborted);
            if (!document.RootElement.TryGetProperty("approve", out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new JsonException();
            approve = value.GetBoolean();
        }
        catch (JsonException)
        {
            await Message(context, StatusCodes.Status400BadRequest, "Send {\"approve\": true} or {\"approve\": false}.");
            return;
        }

        var service = context.RequestServices.GetRequiredService<ApprovalService>();
        var id = context.Request.RouteValues["id"] as string ?? string.Empty;
        var outcome = await service.DecideAsync(id, approve, context.User, context.RequestAborted);
        await Json(context, outcome.Status, outcome.Approval, outcome.Message);
    }

    private static async Task<bool> CanChallenge(HttpContext context) =>
        context.RequestServices.GetService<IAuthenticationSchemeProvider>() is { } schemes && await schemes.GetDefaultChallengeSchemeAsync() is not null;

    private static bool Wants(HttpRequest request, string mediaType) =>
        request.Headers.Accept.ToString().Contains(mediaType, StringComparison.OrdinalIgnoreCase);

    private static void Secure(HttpResponse response)
    {
        response.Headers.CacheControl = "no-store";
        response.Headers.XFrameOptions = "DENY";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers.XContentTypeOptions = "nosniff";
    }

    private static Task Message(HttpContext context, int status, string message) => Json(context, status, null, message);

    private static async Task Json(HttpContext context, int status, AgentApproval? approval, string? message)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            if (approval is not null)
            {
                w.WriteString("id", approval.Id);
                w.WriteString("tool", approval.Tool);
                w.WriteString("message", approval.Message);
                w.WriteString("status", ApprovalService.Describe(approval.Status));
                w.WriteNumber("approvals", approval.ApprovedBy.Count);
                w.WriteNumber("required", approval.RequiredApprovals);
                w.WriteString("expires", approval.Expires);
            }

            if (message is not null) w.WriteString("result", message);
            w.WriteEndObject();
        }

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.Body.WriteAsync(stream.ToArray(), context.RequestAborted);
    }

    private static async Task Page(HttpContext context, int status, string title, string body, string? script)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        context.Response.Headers.ContentSecurityPolicy =
            $"default-src 'none'; style-src 'nonce-{nonce}'; script-src 'nonce-{nonce}'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
        context.Response.StatusCode = status;
        context.Response.ContentType = "text/html; charset=utf-8";
        var html = new StringBuilder()
            .Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            .Append("<meta name=\"robots\" content=\"noindex\">")
            .Append("<title>").Append(Html(title)).Append("</title>")
            .Append("<style nonce=\"").Append(nonce).Append("\">").Append(Style).Append("</style></head>")
            .Append("<body><main><h1>").Append(Html(title)).Append("</h1>").Append(body).Append("</main>");
        if (script is not null) html.Append("<script nonce=\"").Append(nonce).Append("\">").Append(script).Append("</script>");
        html.Append("</body></html>");
        await context.Response.WriteAsync(html.ToString(), context.RequestAborted);
    }

    private static string Html(string text) => HtmlEncoder.Default.Encode(text);

    private static string Label(AgentApprovalTexts texts, string status) => status switch
    {
        "approved" => texts.Allowed,
        "rejected" => texts.Denied,
        _ => texts.Pending,
    };

    /// <summary>The script, with the status labels and the count format as JSON (escaped for a script element).</summary>
    private static string Script(AgentApprovalTexts texts)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteString("pending", texts.Pending);
            w.WriteString("approved", texts.Allowed);
            w.WriteString("rejected", texts.Denied);
            w.WriteString("count", texts.Count);
            w.WriteEndObject();
        }

        return "const labels=" + Encoding.UTF8.GetString(stream.ToArray()) + ";" + ScriptBody;
    }

    private const string Style =
        ":root{color-scheme:light dark;font-family:system-ui,sans-serif}body{margin:0;padding:16px}" +
        "main{max-width:36rem;margin:2rem auto}.message{font-size:1.25rem;padding:1rem;border-left:4px solid #b45309;background:color-mix(in srgb,#b45309 10%,transparent)}" +
        "dl{display:grid;grid-template-columns:auto 1fr;gap:.25rem 1rem}dt{font-weight:600}dd{margin:0}" +
        "button{font:inherit;padding:.6rem 1.4rem;margin-right:.5rem;border-radius:.4rem;border:1px solid currentColor;cursor:pointer}" +
        "button[data-approve=true]{background:#166534;color:#fff;border-color:#166534}.deny{background:transparent}" +
        ".question{font-weight:600;margin-bottom:.25rem}.detail{opacity:.75;font-size:.9rem}";

    private const string ScriptBody =
        "for(const b of document.querySelectorAll('button[data-approve]')){b.addEventListener('click',async()=>{" +
        "for(const x of document.querySelectorAll('button'))x.disabled=true;" +
        "const r=await fetch(location.pathname,{method:'POST',headers:{'Content-Type':'application/json'},credentials:'same-origin'," +
        "body:JSON.stringify({approve:b.dataset.approve==='true'})});const j=await r.json();" +
        "document.getElementById('result').textContent=j.result||'';if(j.status)document.getElementById('status').textContent=labels[j.status]||j.status;" +
        "if(j.required!==undefined)document.getElementById('count').textContent=labels.count.replace('{0}',j.approvals).replace('{1}',j.required);" +
        "if(r.status>=500){for(const x of document.querySelectorAll('button'))x.disabled=false;}});}";
}
