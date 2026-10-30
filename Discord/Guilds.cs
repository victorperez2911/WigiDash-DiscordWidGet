using System.Text.Json;

namespace WigiDash.DiscordWidget.Discord;

/// <summary>Servidor, como vem em GET_GUILDS.</summary>
public sealed record Guild(string Id, string Name);

/// <summary>Tipos de canal do Discord que o plugin usa.</summary>
public enum ChannelKind
{
    Text = 0,
    Voice = 2,
    Announcement = 5,
    Stage = 13,
    Other = -1,
}

/// <summary>Canal, como vem em GET_CHANNELS.</summary>
public sealed record Channel(string Id, string Name, ChannelKind Kind)
{
    public bool IsVoice => Kind is ChannelKind.Voice or ChannelKind.Stage;
    public bool IsText => Kind is ChannelKind.Text or ChannelKind.Announcement;
}

/// <summary>
/// Alguém na call, com o volume e o mute que <b>você</b> deu a essa pessoa (SET_USER_VOICE_SETTINGS) e o avatar
/// no CDN do Discord.
/// </summary>
public sealed record VoiceUser(string Id, string Name, double Volume, bool LocallyMuted, bool SelfMuted, bool SelfDeafened,
    string? AvatarUrl = null);

/// <summary>A call em que você está (GET_SELECTED_VOICE_CHANNEL).</summary>
public sealed record VoiceChannelInfo(string Id, string Name, string? GuildId, IReadOnlyList<VoiceUser> Users)
{
    /// <summary><c>null</c> quando você não está em nenhuma call.</summary>
    public static VoiceChannelInfo? Parse(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("id", out var id))
            return null;
        var users = new List<VoiceUser>();
        if (data.TryGetProperty("voice_states", out var states) && states.ValueKind == JsonValueKind.Array)
        {
            foreach (var state in states.EnumerateArray())
                users.Add(ParseUser(state));
        }
        return new VoiceChannelInfo(
            id.GetString()!,
            data.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
            data.TryGetProperty("guild_id", out var guild) && guild.ValueKind == JsonValueKind.String ? guild.GetString() : null,
            users);
    }

    internal static VoiceUser ParseUser(JsonElement state)
    {
        var user = state.GetProperty("user");
        var voice = state.TryGetProperty("voice_state", out var v) ? v : default;
        var name = Str(state, "nick") ?? Str(user, "global_name") ?? Str(user, "username") ?? "?";
        return new VoiceUser(
            Str(user, "id") ?? string.Empty,
            name,
            state.TryGetProperty("volume", out var volume) && volume.ValueKind == JsonValueKind.Number ? volume.GetDouble() : 100,
            Bool(state, "mute"),
            voice.ValueKind == JsonValueKind.Object && (Bool(voice, "self_mute") || Bool(voice, "mute")),
            voice.ValueKind == JsonValueKind.Object && (Bool(voice, "self_deaf") || Bool(voice, "deaf")),
            AvatarUrl(Str(user, "id"), Str(user, "avatar")));
    }

    /// <summary>Avatar de 64 px; sem avatar próprio, o padrão do Discord ((id >> 22) % 6).</summary>
    internal static string? AvatarUrl(string? userId, string? avatar)
    {
        if (userId is null)
            return null;
        if (avatar is not null)
            return $"https://cdn.discordapp.com/avatars/{userId}/{avatar}.png?size=64";
        var index = ulong.TryParse(userId, out var id) ? (id >> 22) % 6 : 0;
        return $"https://cdn.discordapp.com/embed/avatars/{index}.png";
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
}

internal static class GuildParsing
{
    public static IReadOnlyList<Guild> ParseGuilds(JsonElement data) =>
        data.TryGetProperty("guilds", out var guilds) && guilds.ValueKind == JsonValueKind.Array
            ? guilds.EnumerateArray()
                .Select(g => new Guild(g.GetProperty("id").GetString()!, g.GetProperty("name").GetString() ?? string.Empty))
                .ToList()
            : [];

    public static IReadOnlyList<Channel> ParseChannels(JsonElement data) =>
        data.TryGetProperty("channels", out var channels) && channels.ValueKind == JsonValueKind.Array
            ? channels.EnumerateArray()
                .Select(c => new Channel(
                    c.GetProperty("id").GetString()!,
                    c.GetProperty("name").GetString() ?? string.Empty,
                    c.TryGetProperty("type", out var t) && Enum.IsDefined(typeof(ChannelKind), t.GetInt32())
                        ? (ChannelKind)t.GetInt32()
                        : ChannelKind.Other))
                .ToList()
            : [];
}
