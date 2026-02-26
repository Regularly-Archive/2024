using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Moq;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Domain.Entities;
using PostgreSQL.Embedding.Domain.Models.Planners;
using PostgreSQL.Embedding.Infrastructure.DataAccess;
using PostgreSQL.Embedding.Llm.Connectors.Anthropic;
using PostgreSQL.Embedding.Llm.Planners;
using PostgreSQL.Embedding.Llm.Routers;
using PostgreSQL.Embedding.Llm.Services;
using PostgreSQL.Embedding.Utils;
using Shouldly;
using System.Reflection;
using Wikit.Tests.Utils;

namespace Wikit.Tests.Agents
{
    public class When_Call_TaskPlanner
    {
        private readonly Kernel _kernel;
        private readonly TaskPlanner _taskPlanner;
        private readonly PromptTemplateService _promptTemplateService;

        public When_Call_TaskPlanner()
        {
            var assemblies = new List<Assembly> { Assembly.Load("PostgreSQL.Embedding") };

            // Load Anthropic environment configuration
            var (baseUrl, apiKey, modelName) = TestEnvHelper.GetAnthropicConfig();

            var llmModel = new LlmModel()
            {
                ModelName = modelName,
                ApiKey = apiKey,
                BaseUrl = baseUrl,
                ModelType = (int)ModelType.TextGeneration,
                ServiceProvider = (int)LlmServiceProvider.Anthropic,
                ApiFormat = (int)LlmApiFormat.Anthropic
            };

            var llmRouter = new LlmCompletionRouter(llmModel, Options.Create(new LlmConfig()));
            var httpClient = new HttpClient(llmRouter);

            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.Services.AddLogging();
            kernelBuilder.Services.AddScoped<IRepository<LlmPlugin>>(_ => new Mock<IRepository<LlmPlugin>>().Object);
            kernelBuilder.Services.AddHttpClient();
            kernelBuilder.Services.AddScoped<AgentExecutionContext>();
            kernelBuilder.Services.AddSingleton<IHttpContextAccessor>(_ =>
            {
                var httpContextAccessor = new Mock<IHttpContextAccessor>();
                httpContextAccessor.Setup(x => x.HttpContext).Returns(new DefaultHttpContext());
                return httpContextAccessor.Object;
            });
            kernelBuilder.Services.AddSingleton<IWebHostEnvironment>(_ =>
            {
                var env = new Mock<IWebHostEnvironment>();
                env.Setup(x => x.ContentRootPath).Returns(AppContext.BaseDirectory);
                env.Setup(x => x.WebRootPath).Returns(Path.Combine(AppContext.BaseDirectory, "wwwroot"));
                return env.Object;
            });
            kernelBuilder.AddAnthropicChatCompletion(
                modelId: llmModel.ModelName,
                apiKey: llmModel.ApiKey,
                endpoint: llmModel.BaseUrl,
                httpClient: httpClient
            );

            _kernel = kernelBuilder.Build();
            //_kernel.ImportLlmPlugins(_kernel.Services, appId: null, assemblies);
            _taskPlanner = new TaskPlanner(_kernel);
            _promptTemplateService = new PromptTemplateService();
        }


        [Fact]
        public async Task It_Should_Generate_Sub_Tasks_When_Receive_A_Request()
        {
            var input = "写一首关于日本战国的怀古诗歌，要求格式为七言律诗";
            var subTaks = await _taskPlanner.GetSubTasksAsync(input);
            this.ShouldSatisfyAllConditions(
                () => subTaks.ShouldNotBeNull(),
                () => subTaks.Tasks.ShouldNotBeEmpty()
            );
        }

        [Fact]
        public async Task It_Should_Execute_Sub_Tasks_Sequentially()
        {
            var input = "搜索西安和洛阳两个城市的信息，并从地理、历史、文化、政治四个维度进行对比";
            var subTasks = await _taskPlanner.GetSubTasksAsync(input);

            foreach (var subTask in subTasks.Tasks)
            {
                var suffix = string.Empty;
                if (subTask.DependsOn.Any())
                {
                    var dependencies = subTasks.Tasks.Where(x => subTask.DependsOn.Contains(x.Id)).ToList();
                    suffix = $"{JsonConvert.SerializeObject(dependencies)}";
                }

                var planner = new StepwisePlanner(_kernel, _promptTemplateService, new StepwisePlannerConfig() { Suffix = suffix });
                planner.AddVariable("currentTime", DateTime.Now);

                var plan = await planner.CreatePlanAsync();
                plan.OnStepExecute += (trace) => Console.WriteLine(JsonConvert.SerializeObject(trace));
                var kernelResult = await plan.ExecuteAsync(subTask.Description);

                subTask.ExecuteResult = kernelResult;
                subTask.State = PostgreSQL.Embedding.Domain.Models.Planners.TaskState.Completed;
            }

            Console.WriteLine(JsonConvert.SerializeObject(subTasks));
        }

        [Fact]
        public async Task It_Should_Call_Tools_Automatically()
        {
            var chatCompletionService = _kernel.GetRequiredService<IChatCompletionService>();
            var result = await chatCompletionService.GetChatMessageContentAsync(
                "现在几点了",
                new PromptExecutionSettings() { FunctionChoiceBehavior = FunctionChoiceBehavior.Auto() },
                _kernel
            );

            result.ShouldNotBeNull();
        }

        [Fact]
        public async Task It_Should_Generate_Sub_Tasks_When_Given_Url()
        {
            var input = "总结一下以下页面的主要内容：https://blog.yuanpei.me/posts/container-technology-driven-code-sandbox-practice-and-reflection/";

            var taskPlanner = new TaskPlanner(_kernel);
            var subTasks = await taskPlanner.GetSubTasksAsync(input, null, 3);

            var planner = new StepwisePlanner(_kernel, _promptTemplateService, new StepwisePlannerConfig() { MaxIterations = 10 });
            planner.AddVariable("currentTime", DateTime.Now);

            var graphExecutor = new DAGraphExecutor(input, subTasks.Tasks, planner, _kernel, null);
            await graphExecutor.ExecuteAsync();

            this.ShouldSatisfyAllConditions(
                () => subTasks.Tasks.All(x => x.State == PostgreSQL.Embedding.Domain.Models.Planners.TaskState.Completed).ShouldBeTrue()
            );
        }

        [Fact]
        public void It_Should_Build_DAGraph_By_Sub_Tasks_Successfully()
        {
            var subTasks = new List<SubTask>()
            {
                new SubTask() { Id = 0, Name = "A" },
                new SubTask() { Id = 1, Name = "B", DependsOn = new List<int>() { 0 } },
                new SubTask() { Id = 2, Name = "C", DependsOn = new List<int>() { 1 } },
                new SubTask() { Id = 3, Name = "D", DependsOn = new List<int>() { 2 } },
                new SubTask() { Id = 4, Name = "E", DependsOn = new List<int>() { 2, 3, 4 } }
            };

            var graph = new DAGraph<int>();

            foreach (var subTask in subTasks)
            {
                graph.AddNode(subTask.Id);
            }

            foreach (var subTask in subTasks)
            {
                foreach (var neighbor in subTask.DependsOn)
                {
                    graph.AddEdge(neighbor, subTask.Id);
                }
            }

            var result = graph.TopologicalSort();

            this.ShouldSatisfyAllConditions(
                () => result.ShouldBe(new List<int> { 0, 1, 2, 3, 4 })
            );
        }
    }
}
