using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Common.Confirguration;
using PostgreSQL.Embedding.LlmServices;
using Python.Runtime;
using System.ComponentModel;
using System.IO;
using Newtonsoft.Json;
using Jint;
using PostgreSQL.Embedding.Plugins.Abstration;
using NRedisStack.Search;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "一个可以运行 C#、Python、JavaScript 代码的插件")]
    public class CodeInterpreterPlugin : BasePlugin
    {
        private ILogger<CodeInterpreterPlugin> _logger;
        private const string NO_RETURN_VALUE = "There is no return value for the current action, please proceed.";

        public CodeInterpreterPlugin(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
            using var serviceScope = serviceProvider.CreateScope();

            var loggerFactory = serviceScope.ServiceProvider.GetRequiredService<ILoggerFactory>();
            _logger = loggerFactory.CreateLogger<CodeInterpreterPlugin>();

            var options = serviceScope.ServiceProvider.GetRequiredService<IOptions<PythonConfig>>();
            InitPython(options.Value);
        }

        private void InitPython(PythonConfig config)
        {
            if (PythonEngine.IsInitialized) return;

            if (config != null && !string.IsNullOrEmpty(config.PythonLibrary))
                Runtime.PythonDLL = config.PythonLibrary;

            _logger.LogInformation($"Python Runtime is initializing: {config.PythonLibrary}...");

            PythonEngine.Initialize();
            PythonEngine.BeginAllowThreads();

            _logger.LogInformation($"Python Runtime has been initialized.");
        }

        [KernelFunction]
        [Description("运行 Python 代码并输出结果")]
        public Task<string> RunPython([Description("脚本内容")] string script)
        {
            using var gil = Py.GIL();
            using dynamic scope = Py.CreateScope();

            scope.io = Py.Import("io");
            scope.sys = Py.Import("sys");

            dynamic stringIO = scope.io.StringIO();
            scope.sys.stdout = stringIO;

            dynamic execution = scope.Exec(script);

            if (execution.Contains("result"))
            {
                // 有返回值，则直接取 result 即可
                var result = execution.result.ToString();
                return Task.FromResult(result);
            }
            else
            {
                // 无返回值，需要拦截控制台输出
                var output = scope.sys.stdout.getvalue();
                scope.sys.stdout = scope.sys.__stdout__;

                output = output.ToString();
                output = string.IsNullOrEmpty(output) ? NO_RETURN_VALUE : output;
                return Task.FromResult(output);
            }
        }

        [KernelFunction]
        [Description("运行 JavaScript 代码并输出结果")]
        public Task<string> RunJavaScript([Description("脚本内容")] string script)
        {
            var engine = new Engine();
            engine.SetValue("console.log", new Action<object>(Console.WriteLine));

            var output = Console.ReadLine();
            return Task.FromResult(output);
        }
    }
}
