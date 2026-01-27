using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace WigiDash.DiscordWidget.Core
{
    public class DiscordRESTClient : IDisposable
    {
        private readonly HttpClient _httpClient;
        private string _accessToken;

        public DiscordRESTClient(string accessToken)
        {
            _accessToken = accessToken;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "WigiDash-DiscordWidget/1.0");
        }

        // Soundboard: Send/play soundboard sound
        public async Task<bool> SendSoundboardSoundAsync(ulong channelId, string soundId, ulong? sourceGuildId = null)
        {
            var url = $"{DiscordConfig.API_BASE_URL}/channels/{channelId}/send-soundboard-sound";

            var payload = new
            {
                sound_id = soundId,
                source_guild_id = sourceGuildId?.ToString()
            };

            var content = new StringContent(
                JsonConvert.SerializeObject(payload),
                Encoding.UTF8,
                "application/json"
            );

            try
            {
                var response = await _httpClient.PostAsync(url, content);

                // 204 = success, 400 = not in voice channel
                if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }

        // Get soundboard sounds for a guild
        public async Task<List<SoundboardSound>> GetGuildSoundboardSoundsAsync(ulong guildId)
        {
            var url = $"{DiscordConfig.API_BASE_URL}/guilds/{guildId}/soundboard-sounds";

            try
            {
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync();
                return JsonConvert.DeserializeObject<List<SoundboardSound>>(json);
            }
            catch
            {
                return new List<SoundboardSound>();
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    public class SoundboardSound
    {
        [JsonProperty("sound_id")]
        public string SoundId { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("volume")]
        public double Volume { get; set; }

        [JsonProperty("emoji_id")]
        public string EmojiId { get; set; }

        [JsonProperty("emoji_name")]
        public string EmojiName { get; set; }
    }
}
