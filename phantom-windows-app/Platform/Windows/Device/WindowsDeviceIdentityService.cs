using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SecureOverlay.Application.Device;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Domain.Entities;
using SecureOverlay.Platform.Windows.Secrets;

namespace SecureOverlay.Platform.Windows.Device
{
    public sealed class WindowsDeviceIdentityService : IDeviceIdentityService
    {
        private const string DeviceSecretKey = "install_device_secret";

        private readonly IDeviceProfileRepository _repository;
        private readonly ISecretVault _secretVault;

        public WindowsDeviceIdentityService(IDeviceProfileRepository repository, ISecretVault secretVault)
        {
            _repository = repository;
            _secretVault = secretVault;
        }

        public DeviceProfile GetOrCreateProfile()
        {
            var profile = _repository.Load();
            var deviceSecrets = _secretVault.LoadDeviceSecrets();

            if (profile != null && deviceSecrets.TryGetValue(DeviceSecretKey, out var existingSecret) && !string.IsNullOrWhiteSpace(existingSecret))
            {
                profile.LastSeenAtUtc = DateTime.UtcNow;
                profile.MachineFingerprintHash = BuildMachineFingerprintHash(existingSecret);
                profile.SecretFingerprintHint = BuildSecretFingerprintHint(existingSecret);
                _repository.Save(profile);
                return profile;
            }

            var deviceSecret = Guid.NewGuid().ToString("N");
            deviceSecrets[DeviceSecretKey] = deviceSecret;
            _secretVault.SaveDeviceSecrets(deviceSecrets);

            var createdProfile = new DeviceProfile
            {
                InstallId = $"install-{Guid.NewGuid():N}",
                DeviceLabel = BuildDeviceLabel(),
                MachineFingerprintHash = BuildMachineFingerprintHash(deviceSecret),
                SecretFingerprintHint = BuildSecretFingerprintHint(deviceSecret),
                CreatedAtUtc = DateTime.UtcNow,
                LastSeenAtUtc = DateTime.UtcNow
            };

            _repository.Save(createdProfile);
            return createdProfile;
        }

        private static string BuildDeviceLabel()
        {
            var label = Environment.MachineName?.Trim();
            return string.IsNullOrWhiteSpace(label) ? "phantom-device" : label;
        }

        private static string BuildMachineFingerprintHash(string deviceSecret)
        {
            var machineSignals = string.Join("|", new[]
            {
                Environment.MachineName ?? string.Empty,
                Environment.UserDomainName ?? string.Empty,
                Environment.OSVersion.VersionString ?? string.Empty,
                Environment.Is64BitOperatingSystem ? "x64" : "x86",
                Environment.ProcessorCount.ToString(),
                deviceSecret
            });

            return Hash(machineSignals);
        }

        private static string BuildSecretFingerprintHint(string deviceSecret)
        {
            var hash = Hash(deviceSecret);
            return hash.Length >= 12 ? hash.Substring(0, 12) : hash;
        }

        private static string Hash(string raw)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            return Convert.ToHexString(bytes).ToLowerInvariant();
        }
    }
}
