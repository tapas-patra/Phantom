using System.Text.RegularExpressions;

namespace SecureOverlay.Helpers
{
    public static class UserFacingErrorSanitizer
    {
        private static readonly Regex HttpUrlRegex = new(
            @"https?://[^\s)\]}'""<>]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ApiPathRegex = new(
            @"/(?:api|v\d+)(?:/[A-Za-z0-9._~!$&'*+,;=:@%/\-]+)?",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string SanitizeUserFacingError(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return string.Empty;
            }

            var sanitized = HttpUrlRegex.Replace(message, "[link removed]");
            sanitized = ApiPathRegex.Replace(sanitized, "[path removed]");
            return sanitized.Trim();
        }
    }
}
