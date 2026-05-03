using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace AQuestReborn.CustomNpc.GPTApi.Providers
{
    /// <summary>
    /// The original AI provider that hits the Hubujubu server.
    /// This is a direct extraction of the existing GPTRequestSender logic.
    /// </summary>
    public class DefaultProvider : IAiProvider
    {
        private const string Endpoint = "https://ai.hubujubu.com:5696";

        public bool UsesChatFormat => false;
        public string DisplayName => "Default (Built-in)";

        public async Task<string> GenerateResponseAsync(string prompt, string aiName, string userName,
            List<string> stopSequences, string systemPrompt = null,
            List<AiChatMessage> chatMessages = null)
        {
            return await SendRequest(prompt, aiName, userName, stopSequences);
        }

        private async Task<string> SendRequest(string prompt, string aiName, string userName,
            List<string> stopSequences, int failure = 0)
        {
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            var httpWebRequest = (HttpWebRequest)WebRequest.Create(Endpoint);
            httpWebRequest.ContentType = "application/json";
            httpWebRequest.Method = "POST";

            try
            {
                using (var streamWriter = new StreamWriter(httpWebRequest.GetRequestStream()))
                {
                    string json = JsonConvert.SerializeObject(new GPTRequest(userName, prompt, aiName));
                    streamWriter.Write(json);
                }
                var httpResponse = (HttpWebResponse)httpWebRequest.GetResponse();
                using (var streamReader = new StreamReader(httpResponse.GetResponseStream()))
                {
                    var result = await streamReader.ReadToEndAsync();
                    try { SamplePlugin.Plugin.Instance.PluginLog.Debug($"[Default AI] Raw Response: {result}"); } catch { }
                    var response = JsonConvert.DeserializeObject<GPTOpenAIResult>(result);
                    return response?.choices?[0]?.text ?? "";
                }
            }
            catch (Exception e)
            {
                if (failure == 0)
                {
                    try { SamplePlugin.Plugin.Instance.PluginLog.Warning(e, $"[Default AI] Connection failed: {e.Message}"); } catch { }
                }
                if (failure < 10)
                {
                    return await SendRequest(prompt, aiName, userName, stopSequences, failure + 1);
                }
                return "";
            }
        }
    }
}
