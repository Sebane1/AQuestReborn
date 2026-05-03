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
    /// Google Gemini provider using the native Generative Language API.
    /// Uses X-goog-api-key auth and the contents/parts request format.
    /// </summary>
    public class GeminiProvider : IAiProvider
    {
        private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/";
        private readonly string _apiKey;
        private readonly string _model;

        public bool UsesChatFormat => true;
        public string DisplayName => "Google Gemini";

        /// <param name="apiKey">Google AI Studio API key.</param>
        /// <param name="model">Model name (e.g. "gemini-2.0-flash", "gemini-2.5-pro-preview-05-06"). Defaults to "gemini-2.0-flash".</param>
        public GeminiProvider(string apiKey, string model = "gemini-2.0-flash")
        {
            _apiKey = apiKey ?? "";
            _model = string.IsNullOrEmpty(model) ? "gemini-2.0-flash" : model;
        }

        public async Task<string> GenerateResponseAsync(string prompt, string aiName, string userName,
            List<string> stopSequences, string systemPrompt = null,
            List<AiChatMessage> chatMessages = null)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                try { SamplePlugin.Plugin.Instance.PluginLog.Warning("[Gemini] No API key configured."); } catch { }
                return "";
            }

            return await SendRequest(prompt, aiName, userName, stopSequences, systemPrompt, chatMessages, 0);
        }

        private async Task<string> SendRequest(string prompt, string aiName, string userName,
            List<string> stopSequences, string systemPrompt, List<AiChatMessage> chatMessages, int failure)
        {
            try
            {
                string endpoint = BaseUrl + _model + ":generateContent";
                var httpWebRequest = (HttpWebRequest)WebRequest.Create(endpoint);
                httpWebRequest.ContentType = "application/json";
                httpWebRequest.Method = "POST";
                httpWebRequest.Timeout = 30000;
                httpWebRequest.Headers.Add("X-goog-api-key", _apiKey);

                // Build Gemini-format request body
                var contents = new List<object>();

                if (chatMessages != null && chatMessages.Count > 0)
                {
                    foreach (var msg in chatMessages)
                    {
                        // Gemini uses "user" and "model" roles (no "assistant" or "system" in contents)
                        // System instructions go in a separate field
                        if (msg.Role == "system") continue; // handled below

                        string geminiRole = msg.Role == "assistant" ? "model" : "user";
                        contents.Add(new
                        {
                            role = geminiRole,
                            parts = new[] { new { text = msg.Content } }
                        });
                    }
                }
                else
                {
                    // Fallback: send the raw prompt
                    contents.Add(new
                    {
                        role = "user",
                        parts = new[] { new { text = prompt } }
                    });
                }

                // Build the full request
                var requestObj = new Dictionary<string, object>();
                requestObj["contents"] = contents;

                // Add system instruction if provided
                string sysInstruction = systemPrompt;
                if (string.IsNullOrEmpty(sysInstruction) && chatMessages != null)
                {
                    // Extract from chat messages
                    foreach (var msg in chatMessages)
                    {
                        if (msg.Role == "system")
                        {
                            sysInstruction = msg.Content;
                            break;
                        }
                    }
                }
                if (!string.IsNullOrEmpty(sysInstruction))
                {
                    requestObj["systemInstruction"] = new
                    {
                        parts = new[] { new { text = sysInstruction } }
                    };
                }

                // Generation config
                var genConfig = new Dictionary<string, object>
                {
                    { "temperature", 0.85 },
                    { "maxOutputTokens", 200 },
                    { "topP", 0.9 },
                    { "topK", 40 }
                };

                if (stopSequences != null && stopSequences.Count > 0)
                {
                    // Gemini supports up to 5 stop sequences, filter out newlines since Gemini handles them differently
                    var filtered = new List<string>();
                    foreach (var s in stopSequences)
                    {
                        if (s != "\n" && !string.IsNullOrEmpty(s) && filtered.Count < 5)
                            filtered.Add(s);
                    }
                    if (filtered.Count > 0)
                        genConfig["stopSequences"] = filtered;
                }

                requestObj["generationConfig"] = genConfig;

                using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
                {
                    string json = JsonConvert.SerializeObject(requestObj);
                    streamWriter.Write(json);
                }

                var httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
                using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
                {
                    var result = await streamReader.ReadToEndAsync();
                    try { SamplePlugin.Plugin.Instance.PluginLog.Debug($"[Gemini] Raw Response: {result}"); } catch { }

                    // Parse Gemini response: candidates[0].content.parts[0].text
                    var parsed = JObject.Parse(result);

                    // Check for safety filter blocks
                    var finishReason = parsed?["candidates"]?[0]?["finishReason"]?.ToString();
                    if (finishReason == "SAFETY")
                    {
                        try { SamplePlugin.Plugin.Instance.PluginLog.Warning("[Gemini] Response blocked by safety filters."); } catch { }
                        return "";
                    }

                    var text = parsed?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.ToString();
                    return text ?? "";
                }
            }
            catch (WebException webEx) when (webEx.Response is HttpWebResponse resp)
            {
                string errorBody = "";
                try
                {
                    using (var sr = new StreamReader(resp.GetResponseStream()))
                        errorBody = sr.ReadToEnd();
                }
                catch { }

                int statusCode = (int)resp.StatusCode;
                try { SamplePlugin.Plugin.Instance.PluginLog.Warning($"[Gemini] HTTP {statusCode}: {errorBody}"); } catch { }

                if (statusCode == 400 || statusCode == 401 || statusCode == 403 || statusCode == 429)
                {
                    // Auth, bad request, or rate limit errors — don't retry (retrying 429 makes it worse)
                    return "";
                }

                if (failure < 3)
                {
                    await Task.Delay(2000);
                    return await SendRequest(prompt, aiName, userName, stopSequences, systemPrompt, chatMessages, failure + 1);
                }
                return "";
            }
            catch (Exception e)
            {
                if (failure == 0)
                {
                    try { SamplePlugin.Plugin.Instance.PluginLog.Warning(e, $"[Gemini] Request failed: {e.Message}"); } catch { }
                }
                if (failure < 3)
                {
                    await Task.Delay(2000);
                    return await SendRequest(prompt, aiName, userName, stopSequences, systemPrompt, chatMessages, failure + 1);
                }
                return "";
            }
        }
    }
}
