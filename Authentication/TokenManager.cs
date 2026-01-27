using System;
using System.Security.Cryptography;
using System.Text;

namespace WigiDash.DiscordWidget.Authentication
{
    public static class TokenManager
    {
        public static string EncryptToken(string token)
        {
            if (string.IsNullOrEmpty(token))
                return string.Empty;

            try
            {
                var data = Encoding.UTF8.GetBytes(token);
                var encrypted = ProtectedData.Protect(data, null, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(encrypted);
            }
            catch
            {
                return token; // Fallback: return unencrypted
            }
        }

        public static string DecryptToken(string encryptedToken)
        {
            if (string.IsNullOrEmpty(encryptedToken))
                return string.Empty;

            try
            {
                var data = Convert.FromBase64String(encryptedToken);
                var decrypted = ProtectedData.Unprotect(data, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                return encryptedToken; // Fallback: assume it's already decrypted
            }
        }
    }
}
