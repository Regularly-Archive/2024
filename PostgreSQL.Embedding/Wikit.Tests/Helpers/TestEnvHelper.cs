using DotNetEnv;

namespace Wikit.Tests.Utils
{
    /// <summary>
    /// Helper class for loading environment variables from .env file
    /// </summary>
    public static class TestEnvHelper
    {
        private static bool _isLoaded = false;

        /// <summary>
        /// Load environment variables from .env file
        /// Searches in multiple locations for flexibility
        /// </summary>
        public static void LoadEnv()
        {
            if (_isLoaded)
                return;

            var possiblePaths = new[]
            {
                // From bin/Debug/net10.0/ to project root
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".env"),
                // From test output directory
                Path.Combine(AppContext.BaseDirectory, "..", "..", ".env"),
                Path.Combine(AppContext.BaseDirectory, ".env"),
                // From current working directory
                Path.Combine(Directory.GetCurrentDirectory(), ".env"),
                // From base directory
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".env")
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    Env.Load(path);
                    _isLoaded = true;
                    break;
                }
            }

            // If still not loaded, try to load from common project locations
            if (!_isLoaded)
            {
                var projectRoot = FindProjectRoot();
                var envPath = Path.Combine(projectRoot, ".env");
                if (File.Exists(envPath))
                {
                    Env.Load(envPath);
                    _isLoaded = true;
                }
            }
        }

        /// <summary>
        /// Get environment variable, loading .env if not already loaded
        /// </summary>
        public static string GetEnv(string name)
        {
            LoadEnv();
            return Environment.GetEnvironmentVariable(name)
                ?? throw new Exception($"Environment variable '{name}' is not set. Please configure .env file.");
        }

        /// <summary>
        /// Get environment variable with default value if not set
        /// </summary>
        public static string GetEnvOrDefault(string name, string defaultValue)
        {
            LoadEnv();
            return Environment.GetEnvironmentVariable(name) ?? defaultValue;
        }

        /// <summary>
        /// Check if an environment variable is set
        /// </summary>
        public static bool IsEnvSet(string name)
        {
            LoadEnv();
            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name));
        }

        /// <summary>
        /// Get required environment variables for LLM API testing
        /// </summary>
        public static (string BaseUrl, string ApiKey, string ModelName) GetAnthropicConfig()
        {
            return (
                GetEnv("ANTHROPIC_BASE_URL"),
                GetEnv("ANTHROPIC_API_KEY"),
                GetEnv("ANTHROPIC_MODEL_NAME")
            );
        }

        /// <summary>
        /// Get required environment variables for OpenAI API testing
        /// </summary>
        public static (string BaseUrl, string ApiKey, string ModelName) GetOpenAIConfig()
        {
            return (
                GetEnv("OPENAI_BASE_URL"),
                GetEnv("OPENAI_API_KEY"),
                GetEnv("OPENAI_MODEL_NAME")
            );
        }

        /// <summary>
        /// Get required environment variables for DeepSeek API testing
        /// </summary>
        public static (string BaseUrl, string ApiKey, string ModelName) GetDeepSeekConfig()
        {
            return (
                GetEnvOrDefault("DEEPSEEK_BASE_URL", "https://api.deepseek.com"),
                GetEnv("DEEPSEEK_API_KEY"),
                GetEnvOrDefault("DEEPSEEK_MODEL_NAME", "deepseek-chat")
            );
        }

        /// <summary>
        /// Find project root directory by searching for .csproj file
        /// </summary>
        private static string FindProjectRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (dir.GetFiles("*.csproj").Any())
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }
            return AppContext.BaseDirectory;
        }
    }
}
