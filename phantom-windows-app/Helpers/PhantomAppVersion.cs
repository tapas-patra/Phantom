using System.Reflection;

namespace SecureOverlay.Helpers
{
    public static class PhantomAppVersion
    {
        public static string Current { get; } = Resolve();

        private static string Resolve()
        {
            var informational = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(informational))
                return "0.0.0-local";

            var plus = informational.IndexOf('+');
            return plus < 0 ? informational.Trim() : informational[..plus].Trim();
        }
    }
}
