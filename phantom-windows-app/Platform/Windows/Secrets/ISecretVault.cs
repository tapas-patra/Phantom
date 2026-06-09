using System.Collections.Generic;

namespace SecureOverlay.Platform.Windows.Secrets
{
    public interface ISecretVault
    {
        void SaveProviderKeys(Dictionary<string, List<string>> providerKeys);
        Dictionary<string, List<string>> LoadProviderKeys();
        void SaveAuthSecrets(Dictionary<string, string> authSecrets);
        Dictionary<string, string> LoadAuthSecrets();
        void SaveDeviceSecrets(Dictionary<string, string> deviceSecrets);
        Dictionary<string, string> LoadDeviceSecrets();
    }
}
