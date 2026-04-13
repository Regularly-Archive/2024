using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using MongoDB.Driver.Linq;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Extensions;
using PostgreSQL.Embedding.Domain.Models;
using PostgreSQL.Embedding.Domain.Models.Planners;
using PostgreSQL.Embedding.Llm.Core;
using PostgreSQL.Embedding.Llm.Services;

namespace PostgreSQL.Embedding.Llm.Planners
{
    public class DAGraphExecutor
    {
        private readonly string _query;
        private readonly DAGraph<int> _graph;
        private readonly StepwisePlanner _stepwisePlanner;
        private readonly AgentExecutionContext _agentExecutionContext;
        private readonly List<SubTask> _subTasks = new List<SubTask>();
        private readonly CallablePromptTemplate _subTaskPromptTemplate;
        private readonly Kernel _kernel;
        private readonly CitationService _citationService;

        public Func<StepTrace, Task> OnStepChanged { get; set; }

        public DAGraphExecutor(string query, List<SubTask> subTasks, StepwisePlanner stepwisePlanner, Kernel kernel, CitationService citationService)
        {
            _query = query;
            _kernel = kernel;
            _subTasks = subTasks;
            _graph = BuildDAGraph(subTasks);
            _stepwisePlanner = stepwisePlanner;
            _agentExecutionContext = kernel.GetAgentExecutionContext();
            _subTaskPromptTemplate = new PromptTemplateService().LoadTemplate("SubTask.txt");
            _citationService = citationService;
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            var sortedTaskIds = _graph.TopologicalSort();

            var paralleTasks = sortedTaskIds.FindAll(taskId => _graph[taskId].Indegress == 0).Select(async taskId =>
            {
                var subTask = _subTasks.FirstOrDefault(x => x.Id == taskId);
                var taskStates = JsonConvert.SerializeObject(_subTasks.Select(x => new { Id = x.Id, Name = x.Name, Description = x.Description, State = x.State.ToString() }));
                await ExecuteSubTask(_query, subTask, taskStates, cancellationToken);
            });
            await Task.WhenAll(paralleTasks);

            var serialTasks = sortedTaskIds.FindAll(taskId => _graph[taskId].Indegress > 0).OrderBy(taskId => taskId).ToList();
            foreach (var taskId in serialTasks)
            {
                var subTask = _subTasks.FirstOrDefault(x => x.Id == taskId);
                var taskStates = JsonConvert.SerializeObject(_subTasks.Select(x => new { Id = x.Id, Name = x.Name, Description = x.Description, State = x.State.ToString() }));
                await ExecuteSubTask(_query, subTask, taskStates, cancellationToken);
            }

            await PostProcessFinalOutput();
        }

        private async Task ExecuteSubTask(string query, SubTask subTask, string taskStates, CancellationToken cancellationToken)
        {
            if (_kernel.GetAgentExecutionContext().GetAgentState() != AgentState.Running) 
                return;

            _agentExecutionContext.SetStepId(subTask.Id.ToString());
            if (subTask.State == TaskState.Completed && !subTask.AvailableTools.Any() && !string.IsNullOrEmpty(subTask.ExecuteResult))
            {
                await OnStepChanged?.Invoke(subTask.AsStepTrace(_agentExecutionContext.GetMessageId()));
                return;
            }

            var plan = await _stepwisePlanner.CreateReActAgentAsync();
            //? await _stepwisePlanner.CreatePlanAsync(null, subTask.AvailableTools)
            //: await _stepwisePlanner.CreatePlanAsync();

            plan.OnStepExecute = async (stepTrace) =>
            {
                await OnStepChanged?.Invoke(stepTrace);
            };

            subTask.State = TaskState.InProgress;
            await OnStepChanged?.Invoke(subTask.AsStepTrace(_agentExecutionContext.GetMessageId()));

            if (!subTask.DependsOn.Any())
            {
                var chatHistory = new ChatHistory();

                var context = await BuildSubTaskContext(query, subTask, taskStates, []);
                chatHistory.AddAssistantMessage($"{context}");

                var result = await plan.ExecuteAsync(subTask.Description, chatHistory, cancellationToken);
                subTask.ExecuteResult = result;
                subTask.State = string.IsNullOrEmpty(result) ? Domain.Models.Planners.TaskState.Failed : Domain.Models.Planners.TaskState.Completed;
            }
            else
            {
                var dependencies = _subTasks.FindAll(x => subTask.DependsOn.Contains(x.Id));
                if (dependencies.All(x => x.State == Domain.Models.Planners.TaskState.Completed))
                {
                    var chatHistory = new ChatHistory();

                    var context = await BuildSubTaskContext(query, subTask, taskStates, dependencies);
                    chatHistory.AddAssistantMessage($"{context}");

                    var result = await plan.ExecuteAsync(subTask.Description, chatHistory);
                    subTask.ExecuteResult = result;
                    subTask.State = string.IsNullOrEmpty(result) ? Domain.Models.Planners.TaskState.Failed : Domain.Models.Planners.TaskState.Completed;
                }
            }

            await OnStepChanged?.Invoke(subTask.AsStepTrace(_agentExecutionContext.GetMessageId()));
        }

        private DAGraph<int> BuildDAGraph(List<SubTask> subTasks)
        {
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

            return graph;
        }

        private Task<string> BuildSubTaskContext(string query, SubTask subTask, string taskStates, List<SubTask> dependencies)
        {
            _subTaskPromptTemplate.AddVariable("goal", query);
            _subTaskPromptTemplate.AddVariable("currentTask", subTask.Description);
            _subTaskPromptTemplate.AddVariable("taskStates", taskStates);
            _subTaskPromptTemplate.AddVariable("dependencies", JsonConvert.SerializeObject(dependencies.Select(x => new { Id = x.Id, Name = x.Name, Description = x.Description, State = x.State.ToString(), Output = x.ExecuteResult })));
            _subTaskPromptTemplate.AddVariable("requiredArtifacts", JsonConvert.SerializeObject(subTask.RequiredArtifacts));
            _subTaskPromptTemplate.AddVariable("outputArtifacts", JsonConvert.SerializeObject(subTask.OutputArtifacts));

            return _subTaskPromptTemplate.RenderTemplateAsync(_kernel.Clone());
        }

        private async Task PostProcessFinalOutput()
        {
            var citations = _agentExecutionContext.GetCitations();
            if (citations.Any())
            {
                var finalTask = _subTasks.OrderByDescending(x => x.Id).FirstOrDefault();
                if (finalTask.State == TaskState.Completed)
                {
                    var plainAnswer = _citationService.RemoveCitations(finalTask.ExecuteResult);
                    finalTask.ExecuteResult = plainAnswer;

                    var uniqueCitations = citations.DistinctBy(x => x.Url).Select((x, i) =>
                    {
                        x.Index = i + 1;
                        return x;
                    }).ToList();

                    var result = await _citationService.ExtractCitations(finalTask.ExecuteResult, uniqueCitations, _kernel);
                    Console.WriteLine(JsonConvert.SerializeObject(result));

                    finalTask.CitationItems = result;
                }
            }
            else
            {
                var finalTask = _subTasks.OrderByDescending(x => x.Id).FirstOrDefault();
                if (finalTask.State == TaskState.Completed)
                {
                    var plainAnswer = _citationService.RemoveCitations(finalTask.ExecuteResult);
                    finalTask.ExecuteResult = plainAnswer;
                }
            }
        }
    }

    public class Node<T> : IEquatable<T>
    {
        public T Value { get; set; }
        public List<Node<T>> IncomingEdges { get; set; } = new List<Node<T>>();
        public List<Node<T>> OutgoingEdges { get; set; } = new List<Node<T>>();

        public int Indegress => IncomingEdges.Count;

        public Node(T value)
        {
            Value = value;
        }

        public bool Equals(T? other)
        {
            if (Value == null || other == null) return false;
            return Value.Equals(other);
        }
    }

    public class DAGraph<T>
    {
        private readonly Dictionary<T, Node<T>> _nodes = new Dictionary<T, Node<T>>();

        public Node<T> this[T key]
        {
            get => _nodes[key];
        }

        public List<T> TopologicalSort()
        {
            var result = new List<T>();
            var indegree = new Dictionary<Node<T>, int>();
            var queue = new Queue<Node<T>>();

            foreach (var node in _nodes.Values)
            {
                indegree[node] = node.IncomingEdges.Count;
                if (indegree[node] == 0)
                {
                    queue.Enqueue(node);
                }
            }

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                result.Add(current.Value);

                foreach (var neighbor in current.OutgoingEdges)
                {
                    indegree[neighbor]--;
                    if (indegree[neighbor] == 0)
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }


            if (result.Count != _nodes.Count)
                throw new InvalidOperationException("Graph contains a cycle");

            return result;
        }


        public void AddNode(T value)
        {
            if (_nodes.ContainsKey(value)) return;

            _nodes[value] = new Node<T>(value);
        }

        public bool AddEdge(T from, T to)
        {
            if (!_nodes.ContainsKey(from) || !_nodes.ContainsKey(to))
                return false;

            var source = _nodes[from];
            var target = _nodes[to];

            source.OutgoingEdges.Add(target);
            target.IncomingEdges.Add(source);

            if (HasCycle())
            {
                source.OutgoingEdges.Remove(target);
                target.IncomingEdges.Remove(source);
                return false;
            }

            return true;
        }

        public bool HasCycle()
        {
            var visited = new Dictionary<Node<T>, bool>();
            var recursionStack = new HashSet<Node<T>>();

            foreach (var node in _nodes.Values)
            {
                if (CheckCycle(node, visited, recursionStack))
                {
                    return true;
                }
            }
            return false;
        }

        private bool CheckCycle(
            Node<T> node,
            Dictionary<Node<T>, bool> visited,
            HashSet<Node<T>> recursionStack
        )
        {
            if (recursionStack.Contains(node))
                return true;

            if (visited.ContainsKey(node) && visited[node])
                return false;

            visited[node] = true;
            recursionStack.Add(node);

            foreach (var neighbor in node.OutgoingEdges)
            {
                if (CheckCycle(neighbor, visited, recursionStack))
                {
                    return true;
                }
            }

            recursionStack.Remove(node);
            return false;
        }
    }
}
