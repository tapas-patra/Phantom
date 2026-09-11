using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SecureOverlay;

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
                return PostJson<TRequest, TResponse>(relativePath, request, bearerToken: null);
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

        protected TResponse PostJson<TRequest, TResponse>(string relativePath, TRequest request, string? bearerToken)
        {
            try
            {
                return PostJsonAsync<TRequest, TResponse>(relativePath, request, bearerToken).GetAwaiter().GetResult();
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

        protected async Task<TResponse> PostJsonAsync<TRequest, TResponse>(
            string relativePath,
            TRequest request,
            string? bearerToken = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var message = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}{relativePath}")
                {
                    Content = new StringContent(
                        JsonConvert.SerializeObject(request),
                        Encoding.UTF8,
                        "application/json")
                };

                if (!string.IsNullOrWhiteSpace(bearerToken))
                {
                    message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                }
                AddCorrelationHeaders(message);

                using var response = await HttpClient.SendAsync(message, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new HostedServiceException(FormatFailedRequest(relativePath, response.StatusCode, body));
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
                return GetJsonAsync<TResponse>(relativePath, bearerToken).GetAwaiter().GetResult();
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

        protected async Task<TResponse> GetJsonAsync<TResponse>(
            string relativePath,
            string? bearerToken = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}{relativePath}");
                if (!string.IsNullOrWhiteSpace(bearerToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                }
                AddCorrelationHeaders(request);

                using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new HostedServiceException(FormatFailedRequest(relativePath, response.StatusCode, body));
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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

        private static string FormatFailedRequest(string relativePath, HttpStatusCode statusCode, string? body)
        {
            var detail = string.IsNullOrWhiteSpace(body)
                ? string.Empty
                : " " + (body.Length > 300 ? body[..300] : body);
            return $"Hosted request failed ({(int)statusCode}) for {relativePath}.{detail}";
        }

        private static void AddCorrelationHeaders(HttpRequestMessage request)
        {
            var trace = LiveRequestTrace.Current;
            if (trace == null) return;
            request.Headers.TryAddWithoutValidation("X-Phantom-Correlation-Id", trace.TurnId);
            request.Headers.TryAddWithoutValidation("X-Phantom-Operation-Id", trace.OperationId);
        }

        protected TResponse DeleteJson<TResponse>(string relativePath, string? bearerToken = null)
        {
            try
            {
                return DeleteJsonAsync<TResponse>(relativePath, bearerToken).GetAwaiter().GetResult();
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

        protected async Task<TResponse> DeleteJsonAsync<TResponse>(
            string relativePath,
            string? bearerToken = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete, $"{_baseUrl}{relativePath}");
                if (!string.IsNullOrWhiteSpace(bearerToken))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
                }
                AddCorrelationHeaders(request);

                using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    throw new HostedServiceException(FormatFailedRequest(relativePath, response.StatusCode, body));
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
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
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
    }
}
