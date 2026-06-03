using System;

namespace SecureOverlay.Services
{
    /// <summary>
    /// Factory for creating AI service instances with rotation support
    /// </summary>
    public static class AIServiceFactory
    {
        public static IAIService CreateService(
            string provider, 
            string apiKey, 
            string model)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Log.WriteLine($"⚠️ Empty API key for {provider}");
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                Log.WriteLine($"⚠️ Empty model for {provider}");
            }

            return provider switch
            {
                "ChatGPT" => new ChatGPTService(apiKey, model),
                "Claude" => new ClaudeService(apiKey, model),
                "Mistral" => new MistralService(apiKey, model),
                "Gemini" => new GeminiService(apiKey, model),
                "Groq" => new GroqService(apiKey, model),
                _ => throw new ArgumentException($"Unknown AI provider: {provider}")
            };
        }

        public static IAIService CreateServiceWithRotation(
            string provider,
            APIRotationManager rotationManager)
        {
            var apiKey = rotationManager.GetNextApiKey(provider);
            var model = rotationManager.GetCurrentModel(provider);
            
            // FIX: Wrap ternary operator in parentheses to avoid interpolation conflict
            var keyStatus = rotationManager.GetTotalKeyCount(provider) > 0 ? "configured" : "missing";
            Log.WriteLine($"Creating {provider} service with Key #{keyStatus}, Model: {model}");
            
            return CreateService(provider, apiKey, model);
        }
    }
}
