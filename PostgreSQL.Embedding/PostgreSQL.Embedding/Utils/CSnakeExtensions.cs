using CSnakes.Runtime;
using PostgreSQL.Embedding.Common.Confirguration;

namespace PostgreSQL.Embedding.Utils
{
    public static class CSnakeExtensions
    {
        public static string HomePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Scripts");
        public static string VenvPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Scripts", ".venv");

        public static void AddPythonRuntime(this IServiceCollection services, IConfiguration configuration)
        {
            var config = new PythonConfig();
            configuration.GetSection(nameof(PythonConfig)).Bind(config);

            services.WithPython()
                .WithHome(HomePath)
                .WithVirtualEnvironment(VenvPath)
                .FromFolder(config.PythonExecute, config.PythonVersion)
                .WithPipInstaller();
        }
    }
}
