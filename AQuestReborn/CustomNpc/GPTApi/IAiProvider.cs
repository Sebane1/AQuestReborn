using System.Collections.Generic;
using System.Threading.Tasks;

namespace AQuestReborn.CustomNpc.GPTApi
{
    /// <summary>
    /// Abstraction for AI text generation backends.
    /// Each provider takes a raw text-completion prompt and returns the AI's response text.
    /// </summary>
    public interface IAiProvider
    {
        /// <summary>
        /// Generate a response from the AI model.
        /// </summary>
        /// <param name="prompt">The full text-completion prompt (context + history + final "AiName: ").</param>
        /// <param name="aiName">The NPC's display name (used for stop sequences).</param>
        /// <param name="userName">The player's name (used for stop sequences).</param>
        /// <param name="stopSequences">Sequences that should cause generation to stop.</param>
        /// <param name="systemPrompt">Optional system-level instruction (used by chat-format providers).</param>
        /// <param name="chatMessages">Optional structured chat history (used by chat-format providers).</param>
        /// <returns>The generated response text, or empty string on failure.</returns>
        Task<string> GenerateResponseAsync(string prompt, string aiName, string userName,
            List<string> stopSequences, string systemPrompt = null,
            List<AiChatMessage> chatMessages = null);

        /// <summary>
        /// Whether this provider uses OpenAI-style chat message format instead of raw text completion.
        /// </summary>
        bool UsesChatFormat { get; }

        /// <summary>
        /// Display name for UI.
        /// </summary>
        string DisplayName { get; }
    }

    /// <summary>
    /// A single message in a chat-format conversation.
    /// </summary>
    public class AiChatMessage
    {
        public string Role { get; set; } // "system", "user", "assistant"
        public string Content { get; set; }

        public AiChatMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }
    }
}
