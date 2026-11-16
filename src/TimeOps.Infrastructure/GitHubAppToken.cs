using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using TimeOps.Domain;

namespace TimeOps.Infrastructure;

public sealed class GitHubAppToken(IHttpClientFactory clients, IOptions<GitHubOptions> options, TimeProvider clock)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private string? cached;
    private DateTimeOffset expires;
    public async Task<Result<string>> GetAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (cached is not null && expires > clock.GetUtcNow().AddMinutes(2)) return Result<string>.Success(cached);
            var o = options.Value;
            if (!o.Configured) return Fail("github.configuration", "Configure a organização e a instalação do GitHub App no servidor.");
            using var rsa = RSA.Create();
            rsa.ImportFromPem(await File.ReadAllTextAsync(o.PrivateKeyPath, ct));
            var now = clock.GetUtcNow();
            var jwt = Encode(JsonSerializer.Serialize(new { alg = "RS256", typ = "JWT" })) + "."
                + Encode(JsonSerializer.Serialize(new { iat = now.AddSeconds(-60).ToUnixTimeSeconds(), exp = now.AddMinutes(8).ToUnixTimeSeconds(), iss = o.AppId }));
            jwt += "." + Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(jwt), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            using var request = new HttpRequestMessage(HttpMethod.Post, $"app/installations/{Uri.EscapeDataString(o.InstallationId)}/access_tokens");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            using var response = await clients.CreateClient("GitHub").SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return Fail("github.authentication", "Não foi possível autenticar a instalação do GitHub App. Confira a configuração e as permissões.");
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            cached = body.RootElement.GetProperty("token").GetString()!;
            expires = body.RootElement.GetProperty("expires_at").GetDateTimeOffset();
            return Result<string>.Success(cached);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or HttpRequestException or JsonException or InvalidOperationException)
        { return Fail("github.authentication", "Não foi possível autenticar o GitHub App. Confira a chave e a conexão do servidor."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { return Fail("github.timeout", "O GitHub demorou para responder. Tente novamente."); }
        finally { gate.Release(); }
    }
    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static Result<string> Fail(string code, string message) => Result<string>.Failure(new(code, ErrorCategory.Unavailable, message));
}
