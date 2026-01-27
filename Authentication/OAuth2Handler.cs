using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace WigiDash.DiscordWidget.Authentication
{
    public class OAuth2Handler
    {
        private readonly HttpClient _httpClient;

        public OAuth2Handler()
        {
            _httpClient = new HttpClient();
        }

        public async Task<AuthenticationResult> AuthenticateAsync()
        {
            try
            {
                // Generate PKCE parameters
                var codeVerifier = GenerateCodeVerifier();
                var codeChallenge = GenerateCodeChallenge(codeVerifier);
                var state = GenerateState();

                // Build authorization URL
                var authUrl = BuildAuthorizationUrl(codeChallenge, state);

                // Start HTTP listener for callback
                using (var listener = new HttpListener())
                {
                    listener.Prefixes.Add(DiscordConfig.REDIRECT_URI + "/");

                    try
                    {
                        listener.Start();
                    }
                    catch (HttpListenerException)
                    {
                        return new AuthenticationResult
                        {
                            Success = false,
                            Error = "Failed to start HTTP listener. Please run as administrator or configure URL ACL."
                        };
                    }

                    // Open browser
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = authUrl,
                        UseShellExecute = true
                    });

                    // Wait for callback (with timeout)
                    var contextTask = listener.GetContextAsync();
                    var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5));

                    var completedTask = await Task.WhenAny(contextTask, timeoutTask);

                    if (completedTask == timeoutTask)
                    {
                        return new AuthenticationResult
                        {
                            Success = false,
                            Error = "Authentication timeout"
                        };
                    }

                    var context = await contextTask;
                    var request = context.Request;
                    var response = context.Response;

                    // Parse callback parameters
                    var queryParams = ParseQueryString(request.Url.Query);

                    // Verify state
                    if (!queryParams.TryGetValue("state", out var returnedState) || returnedState != state)
                    {
                        SendResponse(response, "Invalid state parameter", 400);
                        return new AuthenticationResult
                        {
                            Success = false,
                            Error = "Invalid state parameter"
                        };
                    }

                    // Check for error
                    if (queryParams.TryGetValue("error", out var error))
                    {
                        var errorDescription = queryParams.TryGetValue("error_description", out var desc) ? desc : error;
                        SendResponse(response, $"Authentication error: {errorDescription}", 400);
                        return new AuthenticationResult
                        {
                            Success = false,
                            Error = errorDescription
                        };
                    }

                    // Get authorization code
                    if (!queryParams.TryGetValue("code", out var code))
                    {
                        SendResponse(response, "No authorization code received", 400);
                        return new AuthenticationResult
                        {
                            Success = false,
                            Error = "No authorization code received"
                        };
                    }

                    // Exchange code for token
                    var tokenResult = await ExchangeCodeForTokenAsync(code, codeVerifier);

                    if (tokenResult.Success)
                    {
                        SendResponse(response, "Authentication successful! You can close this window.", 200);
                    }
                    else
                    {
                        SendResponse(response, $"Token exchange failed: {tokenResult.Error}", 400);
                    }

                    return tokenResult;
                }
            }
            catch (Exception ex)
            {
                return new AuthenticationResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        private string BuildAuthorizationUrl(string codeChallenge, string state)
        {
            var parameters = new Dictionary<string, string>
            {
                ["client_id"] = DiscordConfig.CLIENT_ID,
                ["redirect_uri"] = DiscordConfig.REDIRECT_URI,
                ["response_type"] = "code",
                ["scope"] = string.Join(" ", DiscordConfig.SCOPES),
                ["state"] = state,
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256"
            };

            var queryString = string.Join("&", parameters.Select(kvp =>
                $"{Uri.EscapeDataString(kvp.Key)}={Uri.EscapeDataString(kvp.Value)}"));

            return $"{DiscordConfig.OAUTH_AUTHORIZE_URL}?{queryString}";
        }

        private async Task<AuthenticationResult> ExchangeCodeForTokenAsync(string code, string codeVerifier)
        {
            try
            {
                var parameters = new Dictionary<string, string>
                {
                    ["client_id"] = DiscordConfig.CLIENT_ID,
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = DiscordConfig.REDIRECT_URI,
                    ["code_verifier"] = codeVerifier
                };

                var content = new FormUrlEncodedContent(parameters);
                var response = await _httpClient.PostAsync(DiscordConfig.OAUTH_TOKEN_URL, content);

                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return new AuthenticationResult
                    {
                        Success = false,
                        Error = $"HTTP {response.StatusCode}: {responseBody}"
                    };
                }

                var tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(responseBody);

                return new AuthenticationResult
                {
                    Success = true,
                    AccessToken = tokenResponse.AccessToken,
                    RefreshToken = tokenResponse.RefreshToken,
                    ExpiresIn = tokenResponse.ExpiresIn
                };
            }
            catch (Exception ex)
            {
                return new AuthenticationResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        private string GenerateCodeVerifier()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Base64UrlEncode(bytes);
        }

        private string GenerateCodeChallenge(string codeVerifier)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
                return Base64UrlEncode(hash);
            }
        }

        private string GenerateState()
        {
            var bytes = new byte[16];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Base64UrlEncode(bytes);
        }

        private string Base64UrlEncode(byte[] data)
        {
            return Convert.ToBase64String(data)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private Dictionary<string, string> ParseQueryString(string query)
        {
            var result = new Dictionary<string, string>();

            if (string.IsNullOrEmpty(query))
                return result;

            query = query.TrimStart('?');

            foreach (var pair in query.Split('&'))
            {
                var parts = pair.Split('=');
                if (parts.Length == 2)
                {
                    result[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1]);
                }
            }

            return result;
        }

        private void SendResponse(HttpListenerResponse response, string message, int statusCode)
        {
            response.StatusCode = statusCode;
            var html = $@"
<!DOCTYPE html>
<html>
<head>
    <title>Discord Authentication</title>
    <style>
        body {{ font-family: Arial, sans-serif; text-align: center; padding: 50px; background: #2C2F33; color: #FFF; }}
        .message {{ font-size: 24px; margin-bottom: 20px; }}
        .success {{ color: #43B581; }}
        .error {{ color: #F04747; }}
    </style>
</head>
<body>
    <div class=""message {(statusCode == 200 ? "success" : "error")}"">{message}</div>
</body>
</html>";

            var buffer = Encoding.UTF8.GetBytes(html);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        public async Task<AuthenticationResult> RefreshTokenAsync(string refreshToken)
        {
            try
            {
                var parameters = new Dictionary<string, string>
                {
                    ["client_id"] = DiscordConfig.CLIENT_ID,
                    ["grant_type"] = "refresh_token",
                    ["refresh_token"] = refreshToken
                };

                var content = new FormUrlEncodedContent(parameters);
                var response = await _httpClient.PostAsync(DiscordConfig.OAUTH_TOKEN_URL, content);

                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return new AuthenticationResult
                    {
                        Success = false,
                        Error = $"HTTP {response.StatusCode}: {responseBody}"
                    };
                }

                var tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(responseBody);

                return new AuthenticationResult
                {
                    Success = true,
                    AccessToken = tokenResponse.AccessToken,
                    RefreshToken = tokenResponse.RefreshToken ?? refreshToken, // Some APIs don't return new refresh token
                    ExpiresIn = tokenResponse.ExpiresIn
                };
            }
            catch (Exception ex)
            {
                return new AuthenticationResult
                {
                    Success = false,
                    Error = ex.Message
                };
            }
        }

        private class TokenResponse
        {
            [JsonProperty("access_token")]
            public string AccessToken { get; set; }

            [JsonProperty("refresh_token")]
            public string RefreshToken { get; set; }

            [JsonProperty("expires_in")]
            public int ExpiresIn { get; set; }

            [JsonProperty("token_type")]
            public string TokenType { get; set; }
        }
    }

    public class AuthenticationResult
    {
        public bool Success { get; set; }
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public int ExpiresIn { get; set; }
        public string Error { get; set; }
    }
}
