using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace AQuestReborn.CustomNpc.GPTApi.Providers
{
    /// <summary>
    /// NovelAI text generation provider using the /ai/generate endpoint.
    /// Requires a persistent API token from the user's NovelAI account.
    /// </summary>
    public class NovelAIProvider : IAiProvider
    {
        private const string Endpoint = "https://text.novelai.net/ai/generate";
        private readonly string _apiToken;
        private readonly string _model;

        public bool UsesChatFormat => false;
        public string DisplayName => "NovelAI";

        /// <param name="apiToken">NovelAI persistent API token from account settings.</param>
        /// <param name="model">Model to use (e.g. "kayra-v2", "erato-v1"). Defaults to "kayra-v2".</param>
        public NovelAIProvider(string apiToken, string model = "kayra-v2")
        {
            _apiToken = apiToken ?? "";
            _model = string.IsNullOrEmpty(model) ? "kayra-v2" : model;
        }

        public async Task<string> GenerateResponseAsync(string prompt, string aiName, string userName,
            List<string> stopSequences, string systemPrompt = null,
            List<AiChatMessage> chatMessages = null)
        {
            if (string.IsNullOrEmpty(_apiToken))
            {
                try { SamplePlugin.Plugin.Instance.PluginLog.Warning("[NovelAI] No API token configured."); } catch { }
                return "";
            }

            return await SendRequest(prompt, aiName, userName, stopSequences, 0);
        }

        private async Task<string> SendRequest(string prompt, string aiName, string userName,
            List<string> stopSequences, int failure)
        {
            try
            {
                var httpWebRequest = (HttpWebRequest)WebRequest.Create(Endpoint);
                httpWebRequest.ContentType = "application/json";
                httpWebRequest.Accept = "application/json";
                httpWebRequest.Method = "POST";
                httpWebRequest.Timeout = 30000;
                httpWebRequest.Headers.Add("Authorization", "Bearer " + _apiToken);

                // Build NovelAI-specific request payload
                var parameters = new Dictionary<string, object>
                {
                    { "temperature", 0.72 },
                    { "max_length", 150 },
                    { "min_length", 1 },
                    { "top_k", 0 },
                    { "top_p", 0.725 },
                    { "top_a", 0.08 },
                    { "typical_p", 0.975 },
                    { "tail_free_sampling", 0.967 },
                    { "repetition_penalty", 2.75 },
                    { "repetition_penalty_range", 2048 },
                    { "repetition_penalty_frequency", 0.02 },
                    { "repetition_penalty_presence", 0.0 },
                    { "use_string", true },
                    { "generate_until_sentence", true },
                };

                // Add stop sequences
                if (stopSequences != null && stopSequences.Count > 0)
                {
                    // NovelAI uses "stop_sequences" as a list of strings
                    parameters["stop_sequences"] = stopSequences;
                }

                var requestBody = new Dictionary<string, object>
                {
                    { "input", prompt },
                    { "model", _model },
                    { "parameters", parameters }
                };

                using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
                {
                    string json = JsonConvert.SerializeObject(requestBody);
                    streamWriter.Write(json);
                }

                var httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
                using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
                {
                    var result = await streamReader.ReadToEndAsync();
                    try { SamplePlugin.Plugin.Instance.PluginLog.Debug($"[NovelAI] Raw Response: {result}"); } catch { }

                    // NovelAI returns { "output": "generated text" }
                    var parsed = JObject.Parse(result);
                    return parsed["output"]?.ToString() ?? "";
                }
            }
            catch (WebException webEx) when (webEx.Response is HttpWebResponse resp)
            {
                int statusCode = (int)resp.StatusCode;
                if (statusCode == 401 || statusCode == 402)
                {
                    try { SamplePlugin.Plugin.Instance.PluginLog.Warning($"[NovelAI] Auth error (HTTP {statusCode}). Check your API token and subscription."); } catch { }
                    return "";
                }

                if (failure == 0)
                {
                    try { SamplePlugin.Plugin.Instance.PluginLog.Warning(webEx, $"[NovelAI] Request failed (HTTP {statusCode}): {webEx.Message}"); } catch { }
                }
                if (failure < 3)
                {
                    await Task.Delay(1000);
                    return await SendRequest(prompt, aiName, userName, stopSequences, failure + 1);
                }
                return "";
            }
            catch (Exception e)
            {
                if (failure == 0)
                {
                    try { SamplePlugin.Plugin.Instance.PluginLog.Warning(e, $"[NovelAI] Request failed: {e.Message}"); } catch { }
                }
                if (failure < 3)
                {
                    await Task.Delay(1000);
                    return await SendRequest(prompt, aiName, userName, stopSequences, failure + 1);
                }
                return "";
            }
        }
    }
}
