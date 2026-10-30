using System.Text.Json;

namespace WigiDash.DiscordWidget.Discord;

/// <summary>Notificação do Discord (evento NOTIFICATION_CREATE).</summary>
public sealed record DiscordNotification(string? ChannelId, string Title, string Body, DateTimeOffset ReceivedAt)
{
    public static DiscordNotification Parse(JsonElement data, DateTimeOffset now)
    {
        var title = Str(data, "title");
        var body = Str(data, "body");
        if (data.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.Object)
        {
            body ??= Str(message, "content");
            if (title is null && message.TryGetProperty("author", out var author))
                title = Str(author, "global_name") ?? Str(author, "username");
        }
        return new DiscordNotification(Str(data, "channel_id"), title ?? "Discord", body ?? string.Empty, now);
    }

    internal static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
}

/// <summary>
/// Som do soundboard (GET_SOUNDBOARD_SOUNDS). Os sons padrão do Discord vêm com <c>guild_id</c> "0" e tocam em
/// qualquer call; som de servidor só toca em call de outro servidor com Nitro (o Discord responde "Invalid Sound").
/// </summary>
public sealed record SoundboardSound(string Id, string Name, string? GuildId, string? Emoji, string? EmojiId = null)
{
    public bool IsDefault => IsDefaultGuild(GuildId);

    /// <summary>Imagem colorida do emoji do som: o emoji do servidor no CDN do Discord ou o Twemoji do unicode.</summary>
    public string? EmojiImageUrl => EmojiUrl(Emoji, EmojiId);

    public static string? EmojiUrl(string? emoji, string? emojiId)
    {
        if (!string.IsNullOrEmpty(emojiId))
            return $"https://cdn.discordapp.com/emojis/{emojiId}.png?size=96";
        if (string.IsNullOrEmpty(emoji))
            return null;
        var codepoints = new List<string>();
        for (var i = 0; i < emoji.Length; i += char.IsSurrogatePair(emoji, i) ? 2 : 1)
            codepoints.Add(char.ConvertToUtf32(emoji, i).ToString("x"));
        // O Twemoji tira o seletor de variação (fe0f) dos nomes sem ZWJ.
        if (!codepoints.Contains("200d"))
            codepoints.Remove("fe0f");
        return $"https://cdn.jsdelivr.net/gh/jdecked/twemoji@15.1.0/assets/72x72/{string.Join('-', codepoints)}.png";
    }

    public static bool IsDefaultGuild(string? guildId) => guildId is null or "0";

    public static IReadOnlyList<SoundboardSound> ParseList(JsonElement data)
    {
        var list = data.ValueKind == JsonValueKind.Array ? data
            : data.ValueKind == JsonValueKind.Object && data.TryGetProperty("sounds", out var sounds) ? sounds
            : default;
        if (list.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<SoundboardSound>();
        foreach (var sound in list.EnumerateArray())
        {
            var id = DiscordNotification.Str(sound, "sound_id") ?? DiscordNotification.Str(sound, "id");
            if (id is null)
                continue;
            if (sound.TryGetProperty("available", out var available) && available.ValueKind == JsonValueKind.False)
                continue;
            result.Add(new SoundboardSound(
                id,
                DiscordNotification.Str(sound, "name") ?? id,
                DiscordNotification.Str(sound, "guild_id"),
                DiscordNotification.Str(sound, "emoji_name"),
                DiscordNotification.Str(sound, "emoji_id")));
        }
        return result;
    }
}

/// <summary>Contagens aproximadas de um servidor (REST <c>users/@me/guilds?with_counts=true</c>).</summary>
public sealed record GuildCounts(string Name, int Members, int Online)
{
    public static GuildCounts? Find(JsonElement guilds, string guildId)
    {
        if (guilds.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var guild in guilds.EnumerateArray())
        {
            if (DiscordNotification.Str(guild, "id") != guildId)
                continue;
            return new GuildCounts(
                DiscordNotification.Str(guild, "name") ?? string.Empty,
                guild.TryGetProperty("approximate_member_count", out var members) ? members.GetInt32() : 0,
                guild.TryGetProperty("approximate_presence_count", out var online) ? online.GetInt32() : 0);
        }
        return null;
    }
}
