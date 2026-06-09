using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using SecureOverlay.Infrastructure.Persistence;

namespace SecureOverlay.Platform.Windows.Secrets
{
    public sealed class WindowsSecretVault : ISecretVault
    {
        private const string MasterKeyStateKey = "secret_master_key";
        private const string ProviderKeysStateKey = "secret_provider_keys";
        private const string AuthSecretsStateKey = "secret_auth";
        private const string DeviceSecretsStateKey = "secret_device";

        private readonly SqliteRuntimeStore _store;

        public WindowsSecretVault(SqliteRuntimeStore store)
        {
            _store = store;
        }

        public void SaveProviderKeys(Dictionary<string, List<string>> providerKeys)
        {
            var bundle = new LocalSecretBundle
            {
                ProviderKeys = providerKeys ?? new Dictionary<string, List<string>>()
            };

            SaveEncryptedPayload(ProviderKeysStateKey, JsonConvert.SerializeObject(bundle));
        }

        public Dictionary<string, List<string>> LoadProviderKeys()
        {
            var payload = LoadEncryptedPayload(ProviderKeysStateKey);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return new Dictionary<string, List<string>>();
            }

            var bundle = JsonConvert.DeserializeObject<LocalSecretBundle>(payload);
            return bundle?.ProviderKeys ?? new Dictionary<string, List<string>>();
        }

        public void SaveAuthSecrets(Dictionary<string, string> authSecrets)
        {
            var bundle = new LocalSecretBundle
            {
                AuthSecrets = authSecrets ?? new Dictionary<string, string>()
            };

            SaveEncryptedPayload(AuthSecretsStateKey, JsonConvert.SerializeObject(bundle));
        }

        public Dictionary<string, string> LoadAuthSecrets()
        {
            var payload = LoadEncryptedPayload(AuthSecretsStateKey);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return new Dictionary<string, string>();
            }

            var bundle = JsonConvert.DeserializeObject<LocalSecretBundle>(payload);
            return bundle?.AuthSecrets ?? new Dictionary<string, string>();
        }

        public void SaveDeviceSecrets(Dictionary<string, string> deviceSecrets)
        {
            var bundle = new LocalSecretBundle
            {
                DeviceSecrets = deviceSecrets ?? new Dictionary<string, string>()
            };

            SaveEncryptedPayload(DeviceSecretsStateKey, JsonConvert.SerializeObject(bundle));
        }

        public Dictionary<string, string> LoadDeviceSecrets()
        {
            var payload = LoadEncryptedPayload(DeviceSecretsStateKey);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return new Dictionary<string, string>();
            }

            var bundle = JsonConvert.DeserializeObject<LocalSecretBundle>(payload);
            return bundle?.DeviceSecrets ?? new Dictionary<string, string>();
        }

        private void SaveEncryptedPayload(string stateKey, string plaintext)
        {
            var masterKey = GetOrCreateMasterKey();
            var nonce = RandomNumberGenerator.GetBytes(12);
            var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
            var cipherBytes = new byte[plaintextBytes.Length];
            var tag = new byte[16];

            using (var aes = new AesGcm(masterKey, 16))
            {
                aes.Encrypt(nonce, plaintextBytes, cipherBytes, tag);
            }

            var envelope = new SecretEnvelope
            {
                Nonce = Convert.ToBase64String(nonce),
                Ciphertext = Convert.ToBase64String(cipherBytes),
                Tag = Convert.ToBase64String(tag)
            };

            _store.UpsertPayload(stateKey, JsonConvert.SerializeObject(envelope), DateTime.UtcNow);
        }

        private string? LoadEncryptedPayload(string stateKey)
        {
            var payload = _store.ReadPayload(stateKey);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            var envelope = JsonConvert.DeserializeObject<SecretEnvelope>(payload);
            if (envelope == null)
            {
                return null;
            }

            var masterKey = GetOrCreateMasterKey();
            var nonce = Convert.FromBase64String(envelope.Nonce);
            var cipherBytes = Convert.FromBase64String(envelope.Ciphertext);
            var tag = Convert.FromBase64String(envelope.Tag);
            var plaintextBytes = new byte[cipherBytes.Length];

            using (var aes = new AesGcm(masterKey, 16))
            {
                aes.Decrypt(nonce, cipherBytes, tag, plaintextBytes);
            }

            return Encoding.UTF8.GetString(plaintextBytes);
        }

        private byte[] GetOrCreateMasterKey()
        {
            var protectedKeyPayload = _store.ReadPayload(MasterKeyStateKey);
            if (!string.IsNullOrWhiteSpace(protectedKeyPayload))
            {
                var protectedBytes = Convert.FromBase64String(protectedKeyPayload);
                return ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
            }

            var masterKey = RandomNumberGenerator.GetBytes(32);
            var protectedMasterKey = ProtectedData.Protect(masterKey, null, DataProtectionScope.CurrentUser);
            _store.UpsertPayload(MasterKeyStateKey, Convert.ToBase64String(protectedMasterKey), DateTime.UtcNow);
            return masterKey;
        }

        private sealed class SecretEnvelope
        {
            public string Nonce { get; set; } = string.Empty;
            public string Ciphertext { get; set; } = string.Empty;
            public string Tag { get; set; } = string.Empty;
        }
    }
}
