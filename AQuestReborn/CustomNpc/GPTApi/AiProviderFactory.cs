using AQuestReborn.CustomNpc.GPTApi.Providers;

namespace AQuestReborn.CustomNpc.GPTApi
{
    /// <summary>
    /// Factory that creates the appropriate AI provider based on the user's configuration.
    /// </summary>
    public static class AiProviderFactory
    {
        /// <summary>
        /// Creates an IAiProvider instance based on the current plugin configuration.
        /// Falls back to DefaultProvider if the config is missing or invalid.
        /// </summary>
        public static IAiProvider CreateProvider()
        {
            try
            {
                var config = SamplePlugin.Plugin.Instance?.Configuration;
                if (config == null) return new DefaultProvider();

                switch (config.AiProvider)
                {
                    case "openai_compatible":
                        if (!string.IsNullOrEmpty(config.OpenAiCompatibleUrl))
                        {
                            return new OpenAICompatibleProvider(
                                config.OpenAiCompatibleUrl,
                                config.OpenAiCompatibleApiKey,
                                config.OpenAiCompatibleModelName);
                        }
                        break;

                    case "novelai":
                        if (!string.IsNullOrEmpty(config.NovelAiApiToken))
                        {
                            return new NovelAIProvider(
                                config.NovelAiApiToken,
                                config.NovelAiModel);
                        }
                        break;

                    case "default":
                    default:
                        return new DefaultProvider();
                }
            }
            catch
            {
                // If anything goes wrong reading config, fall back safely
            }

            return new DefaultProvider();
        }
    }
}
