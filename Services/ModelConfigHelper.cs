using SecureOverlay.Helpers;

namespace SecureOverlay.Services
{
    public static class ModelConfigHelper
    {
        public static ModelConfig GetConfig(string modelName)
        {
            return AIModelRegistry.GetModelConfig(modelName);
        }
    }
}
