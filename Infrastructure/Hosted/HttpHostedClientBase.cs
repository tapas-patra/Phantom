using System;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;

namespace SecureOverlay.Infrastructure.Hosted
{
    public abstract class HttpHostedClientBase
    {
        private readonly string _baseUrl;
        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        protected HttpHostedClientBase(HostedRuntimeOptions options)
        {
            _baseUrl = options.DesktopBackendBaseUrl.TrimEnd('/');
        }

        protected TResponse PostJson<TRequest, TResponse>(string relativePath, TRequest request)
        {
            var response = HttpClient.PostAsync(
                $"{_baseUrl}{relativePath}",
                new StringContent(
                    JsonConvert.SerializeObject(request),
                    Encoding.UTF8,
                    "application/json")).GetAwaiter().GetResult();

            var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Hosted request failed ({(int)response.StatusCode}) for {relativePath}: {body}");
            }

            var result = JsonConvert.DeserializeObject<TResponse>(body);
            if (result == null)
            {
                throw new InvalidOperationException($"Hosted response was empty for {relativePath}.");
            }

            return result;
        }
    }
}
