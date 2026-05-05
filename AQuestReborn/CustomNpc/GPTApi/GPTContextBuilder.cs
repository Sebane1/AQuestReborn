using System.Collections.Generic;

namespace AQuestReborn.CustomNpc.GPTApi
{
    public class GPTContextBuilder
    {
        string _author = "";
        string _title = "";
        string _genre = "";
        string _aiName = "";
        string _userName = "";
        string _aiTraits = "";
        string _userTraits = "";
        private List<KeyValuePair<string, string>> _memories = new List<KeyValuePair<string, string>>();
        GPTHistory _history;
        private string _setting;

        public GPTHistory History { get => _history; set => _history = value; }
        public List<KeyValuePair<string, string>> Memories { get => _memories; set => _memories = value; }

        public GPTContextBuilder(string author, string title, string genre,
        string aiName, string userName, string aiTraits, string userTraits, GPTHistory history)
        {
            _author = author;
            _title = title;
            _genre = genre;
            _aiName = aiName;
            _userName = userName;
            _aiTraits = aiTraits;
            _userTraits = userTraits;
            _history = history;
        }
        public void UpdateSetting(string setting)
        {
            _setting = setting;
        }
        public void UpdateTitle(string author, string title, string genre)
        {
            _author = author;
            _title = title;
            _genre = genre;
        }
        public void UpdateAITraits(string name, string traits)
        {
            _aiName = name;
            _aiTraits = traits;
        }
        public void UpdateUserTraits(string name, string traits)
        {
            _userName = name.Split(" ")[0];
            _userTraits = traits;
        }
        public void UpdateMemories(List<KeyValuePair<string, string>> memories)
        {
            if (memories != null && memories.Count > 0)
            {
                _memories = memories;
            }
        }
        public override string ToString()
        {
            string context = $"[ Author: {_author}; Title: {_title}; Genre: {_genre}]";
            if (!string.IsNullOrEmpty(_setting))
            {
                context += $"\n----";
                context += $"\n[ Setting ]\n" + _setting;
            }
            if (!string.IsNullOrEmpty(_aiTraits))
            {
                context += $"\n----";
                context += $"\n[ Knowledge: {_aiName} ]\n" + _aiTraits;
            }
            if (!string.IsNullOrEmpty(_userTraits))
            {
                context += $"\n----";
                context += $"\n[ Knowledge: {_userName} ]\n" + _userTraits;
            }
            context += $"\n----";
            context += $"\n[ Knowledge: Chat Summary ]\r\nThe chat summary will always summarize past events between {_userName} and {_aiName} in short digestable form. The summary only references the past.";
            if (_memories != null && _memories.Count > 0)
            {
                foreach (var memory in Memories)
                {
                    context += $"\n----";
                    context += $"\n[ Knowledge: {memory.Key} ]\n" + memory.Value;
                }
            }
            context += $"\n***\n";
            context += $"\n[ Style: roleplaying ]\n";
            context += $"[ Instruction: You exist strictly within the fantasy world of Final Fantasy XIV. You are responding in a live chat window. Write your response in the first person as {_aiName}. Do NOT prepend your name to the response. Use asterisks for physical actions (e.g. *smiles*) and format spoken dialogue using double quotes (e.g. \"Hello there!\"). Do NOT reference real-world concepts, modern technology, or video game mechanics (e.g. patches, UI). Always stay perfectly in character as a resident of Eorzea. ]\n";
            
            context += $"[ Formatting Example ]\n";
            context += $"{_userName}: Hello there!\n";
            context += $"{_aiName}: smiles warmly and bows. \"Greetings, traveler! How may I assist you today?\"\n";
            context += $"{_userName}: hands you a potion. Here, take this.\n";
            context += $"{_aiName}: takes the potion gracefully. \"Oh, thank you kindly! This will surely be of use.\"\n";
            context += $"Chat Summary: {_userName} greeted {_aiName} and gave them a potion. {_aiName} then happily accepted the potion.\n";
            context += $"[ End Example ]\n";
            context += $"[CRITICAL RULE 1: You MUST use double quotation marks (\" \") for ALL spoken dialogue. NEVER use single quotation marks (' ') or markdown for dialogue. The game engine strictly requires double quotes to extract your speech.]\n";
            context += $"[CRITICAL RULE 2: You are ONLY playing the character {_aiName}. NEVER narrate the actions, feelings, or dialogue of {_userName}. Do NOT write from a 3rd-person narrator perspective. ONLY write what {_aiName} does and says.]\n";
            context += $"[CRITICAL RULE 3: NEVER prefix your response with \"{_aiName}:\". Just respond directly with your actions in asterisks and your speech in double quotes.]\n";
            context += $"[CRITICAL RULE 4: If the player asks you to change your outfit, you can magically switch your clothing by outputting the command [glamour:Outfit Name] anywhere in your response, matching the outfit name to their request (e.g. \"I'll change right away! [glamour:Beachwear]\"). ONLY do this if requested!]\n\n";
            foreach (var value in _history.Visible)
            {
                foreach (var message in value)
                {
                    context += message + "\n";
                }
            }
            return context;
        }

        /// <summary>
        /// Returns a system prompt containing all character knowledge and rules for chat-format providers.
        /// </summary>
        public string GetSystemPrompt()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"You are {_aiName}, a character in the fantasy world of Final Fantasy XIV.");
            sb.AppendLine($"Genre: {_genre}. Setting: {_title} by {_author}.");

            if (!string.IsNullOrEmpty(_setting))
            {
                sb.AppendLine($"\n[Current Setting]\n{_setting}");
            }
            if (!string.IsNullOrEmpty(_aiTraits))
            {
                sb.AppendLine($"\n[About {_aiName}]\n{_aiTraits}");
            }
            if (!string.IsNullOrEmpty(_userTraits))
            {
                sb.AppendLine($"\n[About {_userName}]\n{_userTraits}");
            }
            if (_memories != null && _memories.Count > 0)
            {
                foreach (var memory in _memories)
                {
                    sb.AppendLine($"\n[Knowledge: {memory.Key}]\n{memory.Value}");
                }
            }

            sb.AppendLine("\n[Response Rules]");
            sb.AppendLine($"- Write your response in first person as {_aiName}.");
            sb.AppendLine("- Use asterisks for physical actions (e.g. *smiles*).");
            sb.AppendLine("- Use double quotes for spoken dialogue (e.g. \"Hello there!\").");
            sb.AppendLine("- Do NOT reference real-world concepts, modern technology, or video game mechanics.");
            sb.AppendLine("- Stay perfectly in character as a resident of Eorzea.");
            sb.AppendLine("- Keep your responses conversational and brief (maximum of 4 sentences).");
            sb.AppendLine($"- NEVER narrate the actions, feelings, or dialogue of {_userName}.");
            sb.AppendLine($"- Do NOT prefix your response with \"{_aiName}:\".");
            sb.AppendLine($"- If the player asks you to change your outfit, output [glamour:Outfit Name] in your response.");

            return sb.ToString();
        }

        /// <summary>
        /// Converts the conversation history into structured chat messages for OpenAI-compatible providers.
        /// </summary>
        public List<AiChatMessage> ToChatMessages(string latestUserMessage)
        {
            var messages = new List<AiChatMessage>();

            // System prompt with all character knowledge and rules
            messages.Add(new AiChatMessage("system", GetSystemPrompt()));

            // Convert history entries into alternating user/assistant messages
            foreach (var exchange in _history.Visible)
            {
                if (exchange.Count >= 2)
                {
                    // exchange[0] = "UserName: message", exchange[1] = "AiName: response"
                    string userText = exchange[0];
                    string aiText = exchange[1];

                    // Strip the "Name: " prefix
                    int userColon = userText.IndexOf(": ");
                    if (userColon > 0) userText = userText.Substring(userColon + 2);

                    int aiColon = aiText.IndexOf(": ");
                    if (aiColon > 0) aiText = aiText.Substring(aiColon + 2);

                    messages.Add(new AiChatMessage("user", userText));
                    messages.Add(new AiChatMessage("assistant", aiText));
                }
            }

            // Add the latest user message
            if (!string.IsNullOrEmpty(latestUserMessage))
            {
                messages.Add(new AiChatMessage("user", latestUserMessage));
            }

            return messages;
        }
    }
}
