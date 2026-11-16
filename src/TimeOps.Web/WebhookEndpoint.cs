using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TimeOps.Application;
using TimeOps.Domain;
using TimeOps.Infrastructure;
namespace TimeOps.Web;

public static class WebhookEndpoint
{
    public static async Task<IResult> HandleAsync(HttpRequest request, IOptions<GitHubOptions> options, IPlanningStore store, TimeProvider clock, CancellationToken ct)
    {
        var o = options.Value;
        if (o.WebhookSecret.Length < 20) return Results.StatusCode(503);
        if (!Guid.TryParse(request.Headers["X-GitHub-Delivery"], out var delivery)) return Results.BadRequest();
        if (request.ContentLength > 1_048_576) return Results.StatusCode(413);
        using var stream = new MemoryStream();
        var buffer = new byte[8192];
        for (int read; (read = await request.Body.ReadAsync(buffer, ct)) > 0;)
        {
            if (stream.Length + read > 1_048_576) return Results.StatusCode(413);
            await stream.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        var bytes = stream.ToArray();
        var signature = request.Headers["X-Hub-Signature-256"].ToString();
        byte[] supplied;
        try { supplied = signature.StartsWith("sha256=", StringComparison.Ordinal) ? Convert.FromHexString(signature[7..]) : []; }
        catch (FormatException) { return Results.Unauthorized(); }
        if (!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(Encoding.UTF8.GetBytes(o.WebhookSecret), bytes), supplied)) return Results.Unauthorized();
        try
        {
            using var json = JsonDocument.Parse(bytes); var root = json.RootElement;
            if (!root.TryGetProperty("installation", out var installation) || installation.GetProperty("id").ToString() != o.InstallationId
                || !root.TryGetProperty("organization", out var organization) || !organization.GetProperty("login").GetString()!.Equals(o.Organization, StringComparison.OrdinalIgnoreCase))
                return Results.Forbid();
            var eventName = request.Headers["X-GitHub-Event"].ToString();
            if (eventName is not ("issues" or "projects_v2_item")) return Results.Accepted();
            string? Node(string key) => root.TryGetProperty(key, out var node) && node.TryGetProperty("node_id", out var id) ? id.GetString() : null;
            var issue = Node("issue"); var item = Node("projects_v2_item");
            var saved = await store.ChangeAsync(d =>
            {
                if (d.Deliveries.All(x => x.Id != delivery.ToString())) d.Deliveries.Add(new() { Id = delivery.ToString(), IssueNodeId = issue, ProjectItemId = item, ReceivedAt = clock.GetUtcNow() });
                return Result<bool>.Success(true);
            }, ct);
            return saved.IsSuccess ? Results.Accepted() : Results.StatusCode(503);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException) { return Results.BadRequest(); }
    }
}
