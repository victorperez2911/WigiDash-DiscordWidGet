namespace WigiDash.DiscordWidget
{
    public static class DiscordConfig
    {
        // Client ID from Discord Application: WigiDashRemoteControl
        public const string CLIENT_ID = "1282041240036179968";

        // Redirect URI for OAuth2 flow
        public const string REDIRECT_URI = "http://localhost:8080/callback";

        // OAuth2 scopes
        public static readonly string[] SCOPES = new[]
        {
            "identify",           // Basic user info
            "guilds",             // Server list
            "rpc",                // Discord RPC local
            "rpc.voice.read",     // Read voice state
            "rpc.voice.write"     // Control voice (connect/disconnect/mute)
        };

        // Discord API endpoints
        public const string API_BASE_URL = "https://discord.com/api/v10";
        public const string OAUTH_AUTHORIZE_URL = "https://discord.com/api/oauth2/authorize";
        public const string OAUTH_TOKEN_URL = "https://discord.com/api/oauth2/token";

        // RPC configuration
        public const int RPC_PORT_START = 6463;
        public const int RPC_PORT_END = 6472;

        // IMPORTANT: Do NOT set Origin header for Discord RPC WebSocket connections!
        // Discord RPC accepts connections with NO Origin header (identifies as local app).
        // Setting ANY Origin value (including "https://discord.com", "null", etc.) causes error 4001.
    }
}
