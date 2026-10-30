using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace WigiDash.DiscordWidget.Discord;

/// <summary>Token OAuth2 do Discord, como fica salvo (criptografado) no <see cref="AccountStore"/>.</summary>
public sealed record DiscordToken(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("refresh_token")] string RefreshToken,
    [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt,
    [property: JsonPropertyName("scope")] string Scope)
{
    /// <summary>Renova com folga de 5 minutos.</summary>
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt - TimeSpan.FromMinutes(5);
}

/// <summary>
/// Login no Discord sem backend e sem client_secret. Exige que o app do Developer Portal esteja marcado como
/// Public Client. Fluxo (validado no teste de 30/10/2026):
/// AUTHORIZE pelo pipe com <c>code_challenge</c> (PKCE S256) → o Discord mostra o popup → código →
/// <c>oauth2/token</c> com <c>code_verifier</c> → AUTHENTICATE com o access_token.
/// </summary>
public static class DiscordAuth
{
    /// <summary>Os 10 scopes que o plugin da Elgato pede; todos liberados para o dono do app.</summary>
    public static readonly string[] Scopes =
    [
        "rpc", "identify", "guilds",
        "rpc.voice.read", "rpc.voice.write",
        "rpc.video.read", "rpc.video.write",
        "rpc.screenshare.read", "rpc.screenshare.write",
        "rpc.notifications.read",
    ];

    private const string TokenUrl = "https://discord.com/api/oauth2/token";

    /// <summary>Tempo para a pessoa clicar em Autorizar no popup do Discord.</summary>
    private static readonly TimeSpan AuthorizeTimeout = TimeSpan.FromMinutes(3);

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>Pede autorização (abre o popup no Discord) e troca o código por token.</summary>
    public static async Task<DiscordToken> AuthorizeAsync(RpcClient rpc, string clientId, TimeProvider time,
        CancellationToken cancellationToken)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        var scopes = new JsonArray();
        foreach (var scope in Scopes)
            scopes.Add(scope);

        var data = await rpc.SendAsync("AUTHORIZE", new JsonObject
        {
            ["client_id"] = clientId,
            ["scopes"] = scopes,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
        }, timeout: AuthorizeTimeout, cancellationToken: cancellationToken).ConfigureAwait(false);

        var code = data.GetProperty("code").GetString()
            ?? throw new DiscordRpcException(0, "O Discord não devolveu o código de autorização.");

        return await RequestTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["code_verifier"] = verifier,
        }, time, cancellationToken).ConfigureAwait(false);
    }

    public static Task<DiscordToken> RefreshAsync(string clientId, DiscordToken token, TimeProvider time,
        CancellationToken cancellationToken) =>
        RequestTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = token.RefreshToken,
        }, time, cancellationToken);

    /// <summary>AUTHENTICATE na sessão RPC. Devolve o <c>data</c> (scopes, user, expires).</summary>
    public static Task<JsonElement> AuthenticateAsync(RpcClient rpc, DiscordToken token, CancellationToken cancellationToken) =>
        rpc.SendAsync("AUTHENTICATE", new JsonObject { ["access_token"] = token.AccessToken },
            cancellationToken: cancellationToken);

    private static async Task<DiscordToken> RequestTokenAsync(Dictionary<string, string> form, TimeProvider time,
        CancellationToken cancellationToken)
    {
        using var response = await Http.PostAsync(TokenUrl, new FormUrlEncodedContent(form), cancellationToken)
            .ConfigureAwait(false);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var error = body.TryGetProperty("error", out var e) ? e.GetString() : null;
            var description = body.TryGetProperty("error_description", out var d) ? d.GetString() : null;
            throw new DiscordAuthException((int)response.StatusCode, error, description);
        }

        return new DiscordToken(
            body.GetProperty("access_token").GetString()!,
            body.GetProperty("refresh_token").GetString()!,
            time.GetUtcNow().AddSeconds(body.GetProperty("expires_in").GetInt32()),
            body.TryGetProperty("scope", out var scope) ? scope.GetString() ?? string.Empty : string.Empty);
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Falha no endpoint de token do Discord.</summary>
public sealed class DiscordAuthException(int status, string? error, string? description)
    : Exception(Describe(status, error, description))
{
    public int Status { get; } = status;
    public string? Error { get; } = error;

    /// <summary>O refresh_token não vale mais (revogado ou de outro app): precisa autorizar de novo.</summary>
    public bool IsInvalidGrant => Error == "invalid_grant";

    private static string Describe(int status, string? error, string? description) => (error, description) switch
    {
        ("invalid_client", _) => "O app não aceitou a troca do código. No Developer Portal, ligue Public Client (aba OAuth2).",
        ("invalid_request", { } d) when d.Contains("code_verifier", StringComparison.Ordinal) =>
            "O app exige PKCE, mas o Discord não devolveu o código com o desafio.",
        _ => $"Discord recusou o token (HTTP {status}): {error} {description}".Trim(),
    };
}
