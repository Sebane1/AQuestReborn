using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace AQuestReborn.CustomNpc.GPTApi.Providers
{
    /// <summary>
    /// OpenAI-compatible chat completion provider.
    /// Works with CosmoRP, OpenRouter, LM Studio, Oobabooga, Ollama, or any OpenAI-compatible API.
    /// </summary>
    public class OpenAICompatibleProvider : IAiProvider
    {
        private readonly string _baseUrl;
        private readonly string _apiKey;
        private readonly string _modelName;

        public bool UsesChatFormat => true;
        public string DisplayName => "OpenAI Compatible";

        /// <param name="baseUrl">The API base URL (e.g. "https://api.pawan.krd/cosmosrp/v1" or "http://localhost:1234/v1").</param>
        /// <param name="apiKey">API key for authentication. Can be empty for local servers.</param>
        /// <param name="modelName">Model identifier to send in requests (e.g. "cosmosrp", "gpt-4o", "local-model").</param>
        public OpenAICompatibleProvider(string baseUrl, string apiKey, string modelName = "")
        {
            _baseUrl = baseUrl?.TrimEnd('/') ?? "";
            _apiKey = apiKey ?? "";
            _modelName = string.IsNullOrEmpty(modelName) ? "default" : modelName;
        }

        public async Task<string> GenerateResponseAsync(string prompt, string aiName, string userName,
            List<string> stopSequences, string systemPrompt = null,
            List<AiChatMessage> chatMessages = null)
        {
            if (string.IsNullOrEmpty(_baseUrl))
            {
                try { SamplePlugin.Plugin.Instance.PluginLog.Warning("[OpenAI Provider] No base URL configured."); } catch { }
                return "";
            }

            // Build the messages array
            var messages = new List<object>();

            if (chatMessages != null && chatMessages.Count > 0)
            {
                // Use structured chat messages from GPTContextBuilder
                foreach (var msg in chatMessages)
                {
                    messages.Add(new { role = msg.Role, content = msg.Content });
                }
            }
            else
            {
                // Fallback: send the raw prompt as a single user message with system context
                if (!string.IsNullOrEmpty(systemPrompt))
                {
                    messages.Add(new { role = "system", content = systemPrompt });
                }
                messages.Add(new { role = "user", content = prompt });
            }

            var requestBody = new
            {
                model = _modelName,
                messages = messages,
                max_tokens = 200,
                temperature = 0.85,
                top_p = 0.9,
                stop = stopSequences ?? new List<string>(),
                presence_penalty = 0.3,
                frequency_penalty = 0.4
            };

            return await SendRequest(requestBody, 0);
        }

        private async Task<string> SendRequest(object requestBody, int failure)
        {
            try
            {
                string endpoint = _baseUrl + "/chat/completions";
                var httpWebRequest = (HttpWebRequest)WebRequest.Create(endpoint);
                httpWebRequest.ContentType = "application/json";
                httpWebRequest.Method = "POST";
                httpWebRequest.Timeout = 30000;

                if (!string.IsNullOrEmpty(_apiKey))
                {
                    httpWebRequest.Headers.Add("Authorization", "Bearer " + _apiKey);
                }

                using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
                {
                    string json = JsonConvert.SerializeObject(requestBody);
                    streamWriter.Write(json);
                }

                var httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
                using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
                {
                    var result = await streamReader.ReadToEndAsync();
                    try { SamplePlugin.Plugin.Instance.PluginLog.Debug($"[OpenAI Provider] Raw Response: {result}"); } catch { }

                    var response = JsonConvert.DeserializeObject<OpenAIChatResult>(result);
                    return response?.choices?[0]?.message?.content ?? "";
                }
            }
            catch (Exception e)
            {
                if (failure == 0)
                {
                    try { SamplePlugin.Plugin.Instance.PluginLog.Warning(e, $"[OpenAI Provider] Request failed: {e.Message}"); } catch { }
                }
                if (failure < 3)
                {
                    await Task.Delay(1000);
                    return await SendRequest(requestBody, failure + 1);
                }
                return "";
            }
        }

        // Response DTOs for OpenAI chat completion format
        private class OpenAIChatResult
        {
            public List<OpenAIChatChoice> choices { get; set; }
        }
        private class OpenAIChatChoice
        {
            public OpenAIChatMessage message { get; set; }
        }
        private class OpenAIChatMessage
        {
            public string role { get; set; }
            public string content { get; set; }
        }
    }
}
