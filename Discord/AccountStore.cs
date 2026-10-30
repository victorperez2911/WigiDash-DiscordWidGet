using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WigiDash.DiscordWidget.Discord;

/// <summary>
/// Conta do Discord do plugin inteiro (não de uma instância): client_id do app e o token. Fica em
/// <c>%LOCALAPPDATA%\OpenWigiDash\WidgetData\Discord\account.json</c>, com o token criptografado por DPAPI
/// para o usuário do Windows. As configurações da instância não servem aqui: são por widget e são clonadas.
/// </summary>
public sealed class AccountStore(string path)
{
    /// <summary>App WigiDashRemoteControl do Victor (Public Client). Cada pessoa pode trocar pelo próprio.</summary>
    public const string DefaultClientId = "1282041240036179968";

    private static readonly byte[] Entropy = "WigiDash.DiscordWidget"u8.ToArray();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly Lock _lock = new();

    public static AccountStore Default { get; } = new(System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OpenWigiDash", "WidgetData", "Discord", "account.json"));

    public string FilePath { get; } = path;

    public string ClientId
    {
        get => Load().ClientId ?? DefaultClientId;
        set
        {
            lock (_lock)
            {
                var file = Load();
                // Token de outro app não serve para este.
                Save(file with { ClientId = value, Token = file.ClientId == value ? file.Token : null });
            }
        }
    }

    public DiscordToken? LoadToken()
    {
        var protectedToken = Load().Token;
        if (string.IsNullOrEmpty(protectedToken))
            return null;
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedToken), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<DiscordToken>(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            return null;
        }
    }

    public void SaveToken(DiscordToken? token)
    {
        lock (_lock)
        {
            string? value = null;
            if (token is not null)
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(token);
                value = Convert.ToBase64String(ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser));
            }
            Save(Load() with { Token = value });
        }
    }

    private AccountFile Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<AccountFile>(File.ReadAllText(FilePath)) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new();
        }
    }

    private void Save(AccountFile file)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(file, Json), Encoding.UTF8);
        File.Move(temp, FilePath, overwrite: true);
    }

    private sealed record AccountFile
    {
        public string? ClientId { get; init; }
        public string? Token { get; init; }
    }
}
