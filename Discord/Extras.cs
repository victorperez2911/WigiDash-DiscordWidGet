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

/// <summary>Som do soundboard (GET_SOUNDBOARD_SOUNDS). <see cref="GuildId"/> nulo = som padrão do Discord.</summary>
public sealed record SoundboardSound(string Id, string Name, string? GuildId, string? Emoji)
{
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
                DiscordNotification.Str(sound, "emoji_name")));
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
