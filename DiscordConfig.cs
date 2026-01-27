namespace WigiDash.DiscordWidget
{
    public static class DiscordConfig
    {
        // Client ID from Discord Application
        // TODO: Replace with your actual Client ID after creating Discord Application
        public const string CLIENT_ID = "YOUR_CLIENT_ID_HERE";

        // Redirect URI for OAuth2 flow
        public const string REDIRECT_URI = "http://localhost:8080/callback";

        // OAuth2 scopes
        public static readonly string[] SCOPES = new[]
        {
            "identify",     // Basic user info
            "guilds",       // Server list
            "rpc",          // Discord RPC
            "rpc.api"       // RPC API access
        };

        // Discord API endpoints
        public const string API_BASE_URL = "https://discord.com/api/v10";
        public const string OAUTH_AUTHORIZE_URL = "https://discord.com/api/oauth2/authorize";
        public const string OAUTH_TOKEN_URL = "https://discord.com/api/oauth2/token";

        // RPC configuration
        public const int RPC_PORT_START = 6463;
        public const int RPC_PORT_END = 6472;
        public const string RPC_ORIGIN = "https://discord.com";
    }
}
