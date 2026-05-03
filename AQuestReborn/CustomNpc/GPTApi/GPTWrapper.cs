using AQuestReborn.CustomNpc.GPTApi;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AQuestReborn.CustomNpc
{
    public class GPTWrapper : IDisposable
    {
        private string _aiName;
        ConcurrentDictionary<string, GPTContextBuilder> _histories = new ConcurrentDictionary<string, GPTContextBuilder>();
        ConcurrentDictionary<string, int> _persistenceCounter = new ConcurrentDictionary<string, int>();
        MemoryContextManager memoryContextManager;

        public string Personality { get => _aiName; }

        public GPTWrapper(string aiName, string memoryPath)
        {
            _aiName = aiName;
            memoryContextManager = new MemoryContextManager(memoryPath);
        }

        public async Task<string> SendMessage(string name, string message, string aiGreeting, string userDetails, string aiDetails, string setting, int maxContext, string modelChoice = "")
        {
            var newHistory = new GPTHistory(name, userDetails,
                _aiName + ":" + aiGreeting, $"{name} said hello, and {_aiName} responded back with their own greeting.");
            if (!_histories.ContainsKey(name))
            {
                _histories[name] = new GPTContextBuilder("Square Enix", "Final Fantasy XIV", "fantasy",
                _aiName, name, aiDetails, userDetails, newHistory);
                _histories[name].UpdateMemories(memoryContextManager.GetMemoriesInValue(message));
            }
            else
            {
                _histories[name].UpdateAITraits(_aiName, aiDetails);
                _histories[name].UpdateUserTraits(name, userDetails);
                _histories[name].UpdateMemories(memoryContextManager.GetMemoriesInValue((!string.IsNullOrEmpty(userDetails) ? "" : name + " ") + message));
            }
            _histories[name].UpdateSetting(setting);
            string lastValue = _histories.ContainsKey(name) ? _histories[name].History.GetLastVisibleItem() : Guid.NewGuid().ToString();
            string response = await new GPTRequestSender().GetGPTResponse(name, _histories[name].ToString()
                + name.Split(" ")[0] + DetectFormatting(message.Trim()) + "\n" + _aiName + ": ", _aiName, false, modelChoice);

            // If history was cleared while awaiting the response, bail out early
            if (!_histories.ContainsKey(name)) return "";

            // Reject responses that lack both dialogue quotes and action asterisks
            /*if (!string.IsNullOrEmpty(response) && !response.Contains("\"") && !response.Contains("*"))
            {
                return "";
            }*/
            
            // Reject parroting (where the AI echoes the user's message exactly)
            if (!string.IsNullOrEmpty(response))
            {
                string cleanUserMsg = message.Trim().ToLower();
                string finalCleanResp = response.Trim().ToLower();
                if (finalCleanResp == cleanUserMsg || (cleanUserMsg.Length > 15 && finalCleanResp.Contains(cleanUserMsg)))
                {
                    return "";
                }
            }

            if (_histories.ContainsKey(name) && _histories[name].History.Visible.Count > 0)
            {
                var lastMessage = _histories[name].History.Visible[_histories[name].History.Visible.Count - 1];
                if (lastMessage.Count > 1)
                {
                    string lastResponse = lastMessage[1].Replace(_aiName + ":", "").Trim();
                    string currentResponse = response.Trim();
                    var lastTokens = lastResponse.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    var currentTokens = currentResponse.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    
                    if (lastTokens.Length >= 2 && currentTokens.Length >= 2 &&
                        lastTokens[0].ToLower() == currentTokens[0].ToLower() && 
                        lastTokens[1].ToLower() == currentTokens[1].ToLower())
                    {
                        int index = currentResponse.IndexOf(currentTokens[1], currentResponse.IndexOf(currentTokens[0])) + currentTokens[1].Length;
                        response = currentResponse.Substring(index).Trim();
                    }
                    else if (lastTokens.Length >= 1 && currentTokens.Length >= 1 &&
                             lastTokens[0].ToLower() == currentTokens[0].ToLower())
                    {
                        int index = currentResponse.IndexOf(currentTokens[0]) + currentTokens[0].Length;
                        response = currentResponse.Substring(index).Trim();
                    }
                }
            }

            AddToHistory(name, message, response);
            if (_persistenceCounter.ContainsKey(name))
            {
                _persistenceCounter[name]++;
            }
            else
            {
                _persistenceCounter[name] = 1;
            }
            if (_persistenceCounter[name] >= maxContext)
            {
                memoryContextManager.AddConversationalMemory(name, await GetSummary(name));
                _persistenceCounter[name] = 0;
            }
            if (_histories[name].History.Visible.Count > maxContext)
            {
                _histories[name].History.Visible.RemoveAt(1);
            }
            string value = _histories[name].History.GetLastVisibleItem();
            Thread.Sleep(1000);
            return WordFilter(value);
        }

        public void ChangeSetting(string name, string setting)
        {
            _histories[name].UpdateSetting(setting);
        }
        public async Task<string> GetSummary(string name)
        {
            if (!_histories.ContainsKey(name)) return "";
            string lastValue = _histories[name].History.GetLastVisibleItem();
            string response = await new GPTRequestSender().GetGPTResponse(name, _histories[name].ToString()
                + "[Chat Summary:", _aiName, false);
            return response.Replace("[Chat Summary:", null);
        }

        public void Dispose()
        {
            _histories.Clear();
        }
        public async void AddConversationalMemory(string key)
        {
            memoryContextManager.AddConversationalMemory(key, await GetSummary(key));
        }
        internal void ClearHistory(string sender)
        {
            _histories.TryRemove(sender, out var value);
        }
        public List<string> GetConversationalMemory(string name)
        {
            return memoryContextManager.GetConversationalMemory(name);
        }

        public void AddToHistory(string sender, string userText, string botResponse)
        {
            if (_histories.ContainsKey(sender))
            {
                string trimmedUserText = userText.Trim();
                string trimmedBotResponse = botResponse.Trim();

                string lowerResponse = trimmedBotResponse.ToLower();
                if (string.IsNullOrWhiteSpace(lowerResponse) || 
                    lowerResponse == "..." || 
                    lowerResponse == "…" || 
                    lowerResponse.Contains("*silence*") || 
                    lowerResponse.Contains("*silent*"))
                {
                    ClearHistory(sender);
                    return;
                }

                string formattedResponse = DetectFormatting(trimmedBotResponse).Trim().ToLower();

                if (_histories[sender].History.Visible.Count > 0)
                {
                    var lastMessage = _histories[sender].History.Visible[_histories[sender].History.Visible.Count - 1];
                    if (lastMessage.Count > 1)
                    {
                        string cleanLast = lastMessage[1].Replace(_aiName, "").Trim().ToLower();
                        if (cleanLast == formattedResponse)
                        {
                            ClearHistory(sender);
                            return;
                        }
                    }
                }

                _histories[sender].History.Visible.Add(new List<string> {
                sender.Split(" ")[0] + DetectFormatting(trimmedUserText), _aiName + DetectFormatting(trimmedBotResponse) });
            }
        }

        public string WordFilter(string value)
        {
            if (value.Length > 0 && value.StartsWith(_aiName))
            {
                int i = value.IndexOf(" ");
                if (i != -1)
                {
                    value = value.Substring(i + 1);
                }
            }

            // Failsafe to strip any leaked bracketed meta-instructions, even if unclosed, but ignore glamour commands
            value = System.Text.RegularExpressions.Regex.Replace(value, @"\[(?!glamour:).*?(?:\]|$)", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();

            // Strip out garbage repeating asterisks (e.g. *****)
            value = System.Text.RegularExpressions.Regex.Replace(value, @"\*{2,}", "").Trim();

            // Clean up leading colons or rogue spaces from name stripping
            value = value.TrimStart(':', ' ').Trim();

            return value;
        }
        public void AddMemory(string title, string description)
        {
            memoryContextManager.AddMemory(title, description);
        }
        public string DetectFormatting(string value)
        {
            return ": " + value;
        }
    }
}
