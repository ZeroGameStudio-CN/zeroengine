using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace ZeroEngine.AssetCatalog
{
    internal sealed class CatalogChatClient : IDisposable
    {
        private readonly HttpClient _client;
        private readonly string _serviceName;
        private readonly bool _includeHttpStatus;

        internal CatalogChatClient(HttpMessageHandler handler, string serviceName, bool includeHttpStatus = false)
        {
            _client = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
            _client.Timeout = TimeSpan.FromSeconds(90);
            _serviceName = serviceName;
            _includeHttpStatus = includeHttpStatus;
        }

        internal async Task<TAnswer> AskAsync<TAnswer>(Uri endpoint, string apiKey, string requestJson)
        {
            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, endpoint))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                request.Content = new StringContent(requestJson, Encoding.UTF8, "application/json");
                using (HttpResponseMessage response = await _client.SendAsync(request))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        string status = _includeHttpStatus ? $": HTTP {(int)response.StatusCode}" : string.Empty;
                        throw new InvalidOperationException($"{_serviceName} request failed{status}.");
                    }

                    ChatEnvelope envelope;
                    try
                    {
                        envelope = JsonUtility.FromJson<ChatEnvelope>(await response.Content.ReadAsStringAsync());
                    }
                    catch (ArgumentException)
                    {
                        throw new InvalidOperationException($"{_serviceName} response could not be parsed.");
                    }
                    string content = envelope?.choices != null && envelope.choices.Length > 0 ? envelope.choices[0]?.message?.content : null;
                    if (string.IsNullOrWhiteSpace(content))
                        throw new InvalidOperationException($"{_serviceName} response did not contain an answer.");

                    try
                    {
                        return JsonUtility.FromJson<TAnswer>(StripCodeFence(content));
                    }
                    catch (ArgumentException)
                    {
                        throw new InvalidOperationException($"{_serviceName} answer JSON could not be parsed.");
                    }
                }
            }
        }

        public void Dispose() => _client.Dispose();

        private static string StripCodeFence(string value)
        {
            string result = value.Trim();
            if (!result.StartsWith("```", StringComparison.Ordinal)) return result;
            int firstLine = result.IndexOf('\n');
            int lastFence = result.LastIndexOf("```", StringComparison.Ordinal);
            return firstLine >= 0 && lastFence > firstLine ? result.Substring(firstLine + 1, lastFence - firstLine - 1).Trim() : result;
        }

        [Serializable] private sealed class ChatEnvelope { public ChatChoice[] choices; }
        [Serializable] private sealed class ChatChoice { public ChatMessage message; }
        [Serializable] private sealed class ChatMessage { public string content; }
    }
}
