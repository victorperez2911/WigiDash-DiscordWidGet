using System.Text.Json;

namespace WigiDash.DiscordWidget.Discord;

/// <summary>Dispositivo de áudio como o Discord lista em <c>available_devices</c>.</summary>
public sealed record AudioDevice(string Id, string Name);

/// <summary>Entrada ou saída de áudio: dispositivo atual, volume (0-100 entrada, 0-200 saída) e opções.</summary>
public sealed record AudioIo(string DeviceId, double Volume, IReadOnlyList<AudioDevice> Devices)
{
    public static AudioIo Parse(JsonElement element)
    {
        var devices = new List<AudioDevice>();
        if (element.TryGetProperty("available_devices", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var device in list.EnumerateArray())
                devices.Add(new AudioDevice(device.GetProperty("id").GetString() ?? string.Empty,
                    device.GetProperty("name").GetString() ?? string.Empty));
        }
        return new AudioIo(
            element.TryGetProperty("device_id", out var id) ? id.GetString() ?? string.Empty : string.Empty,
            element.TryGetProperty("volume", out var volume) && volume.ValueKind == JsonValueKind.Number ? volume.GetDouble() : 0,
            devices);
    }
}

/// <summary>Modo de entrada de voz do Discord.</summary>
public enum VoiceMode
{
    VoiceActivity,
    PushToTalk,
}

/// <summary>Resultado de GET_VOICE_SETTINGS e do evento VOICE_SETTINGS_UPDATE.</summary>
public sealed record VoiceSettings(bool Mute, bool Deaf, VoiceMode Mode, AudioIo Input, AudioIo Output)
{
    public static VoiceSettings Parse(JsonElement data)
    {
        var mode = data.TryGetProperty("mode", out var m) && m.TryGetProperty("type", out var type)
            && type.GetString() == "PUSH_TO_TALK"
            ? VoiceMode.PushToTalk
            : VoiceMode.VoiceActivity;
        return new VoiceSettings(
            data.TryGetProperty("mute", out var mute) && mute.GetBoolean(),
            data.TryGetProperty("deaf", out var deaf) && deaf.GetBoolean(),
            mode,
            data.TryGetProperty("input", out var input) ? AudioIo.Parse(input) : new AudioIo(string.Empty, 0, []),
            data.TryGetProperty("output", out var output) ? AudioIo.Parse(output) : new AudioIo(string.Empty, 0, []));
    }

    /// <summary>
    /// O evento VOICE_SETTINGS_UPDATE pode vir sem a lista de dispositivos; mantém a última conhecida.
    /// </summary>
    public VoiceSettings MergeDevices(VoiceSettings? previous) => previous is null
        ? this
        : this with
        {
            Input = Input.Devices.Count > 0 ? Input : Input with { Devices = previous.Input.Devices },
            Output = Output.Devices.Count > 0 ? Output : Output with { Devices = previous.Output.Devices },
        };
}
