using System;
using Newtonsoft.Json;
using SecureOverlay.Application.Persistence;
using SecureOverlay.Domain.Entities;
using SecureOverlay.Platform.Windows.Secrets;

namespace SecureOverlay.Infrastructure.Persistence
{
    public sealed class SqliteAuthSessionRepository : IAuthSessionRepository
    {
        private readonly SqliteRuntimeStore _store;
        private readonly ISecretVault _secretVault;

        public SqliteAuthSessionRepository(SqliteRuntimeStore store)
        {
            _store = store;
            _secretVault = new WindowsSecretVault(store);
        }

        public AuthSessionCache? Load()
        {
            var payload = _store.ReadPayload(SqliteRuntimeStore.AuthSessionKey);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            var session = JsonConvert.DeserializeObject<AuthSessionCache>(payload);
            if (session == null)
            {
                return null;
            }

            var authSecrets = _secretVault.LoadAuthSecrets();
            session.AccessToken = authSecrets.TryGetValue("access_token", out var accessToken) ? accessToken : string.Empty;
            session.RefreshToken = authSecrets.TryGetValue("refresh_token", out var refreshToken) ? refreshToken : string.Empty;
            return session;
        }

        public void Save(AuthSessionCache session)
        {
            _secretVault.SaveAuthSecrets(new System.Collections.Generic.Dictionary<string, string>
            {
                ["access_token"] = session.AccessToken ?? string.Empty,
                ["refresh_token"] = session.RefreshToken ?? string.Empty
            });

            var clone = JsonConvert.DeserializeObject<AuthSessionCache>(
                JsonConvert.SerializeObject(session, Formatting.Indented)) ?? new AuthSessionCache();
            clone.AccessToken = string.Empty;
            clone.RefreshToken = string.Empty;

            var payload = JsonConvert.SerializeObject(clone, Formatting.Indented);
            _store.UpsertPayload(SqliteRuntimeStore.AuthSessionKey, payload, DateTime.UtcNow);
        }

        public void Clear()
        {
            _secretVault.SaveAuthSecrets(new System.Collections.Generic.Dictionary<string, string>());
            _store.DeletePayload(SqliteRuntimeStore.AuthSessionKey);
        }
    }
}
