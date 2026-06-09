using System.Collections.Generic;

namespace SecureOverlay.Platform.Windows.Secrets
{
    public sealed class LocalSecretBundle
    {
        public Dictionary<string, List<string>> ProviderKeys { get; set; } = new Dictionary<string, List<string>>();
        public Dictionary<string, string> AuthSecrets { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> DeviceSecrets { get; set; } = new Dictionary<string, string>();
    }
}
