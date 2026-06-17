using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json.Linq;
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
            try
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
                    var errorMessage = TryExtractErrorMessage(body);
                    throw new HostedServiceException(
                        string.IsNullOrWhiteSpace(errorMessage)
                            ? $"Hosted request failed ({(int)response.StatusCode}) for {relativePath}."
                            : errorMessage);
                }

                var result = JsonConvert.DeserializeObject<TResponse>(body);
                if (result == null)
                {
                    throw new HostedServiceException($"Hosted response was empty for {relativePath}.");
                }

                return result;
            }
            catch (HostedServiceException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new HostedServiceException(
                    $"Hosted request failed for {relativePath}. Verify backend reachability and configuration.",
                    ex);
            }
        }

        protected TResponse GetJson<TResponse>(string relativePath, string? bearerToken = null)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}{relativePath}");
                if (!string.IsNullOrWhiteSpace(bearerToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                }

                var response = HttpClient.SendAsync(request).GetAwaiter().GetResult();
                var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                {
                    var errorMessage = TryExtractErrorMessage(body);
                    throw new HostedServiceException(
                        string.IsNullOrWhiteSpace(errorMessage)
                            ? $"Hosted request failed ({(int)response.StatusCode}) for {relativePath}."
                            : errorMessage);
                }

                var result = JsonConvert.DeserializeObject<TResponse>(body);
                if (result == null)
                {
                    throw new HostedServiceException($"Hosted response was empty for {relativePath}.");
                }

                return result;
            }
            catch (HostedServiceException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new HostedServiceException(
                    $"Hosted request failed for {relativePath}. Verify backend reachability and configuration.",
                    ex);
            }
        }

        private static string? TryExtractErrorMessage(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            try
            {
                var payload = JObject.Parse(body);
                return payload["error"]?.Value<string>();
            }
            catch
            {
                return body;
            }
        }
    }
}
