using Microsoft.SemanticKernel.ChatCompletion;
using Newtonsoft.Json;
using PostgreSQL.Embedding.Common.Models.Planners;

namespace PostgreSQL.Embedding.Planners
{
    public class DAGraphExecutor
    {
        private readonly string _query;
        private readonly DAGraph<int> _graph;
        private readonly StepwisePlanner _stepwisePlanner;
        private readonly List<SubTask> _subTasks = new List<SubTask>();
        
        public Action<StepTrace> OnStepChanged { get; set; }

        public DAGraphExecutor(string query, List<SubTask> subTasks, StepwisePlanner stepwisePlanner)
        {
            _query = query;
            _subTasks = subTasks;
            _graph = BuildDAGraph(subTasks);
            _stepwisePlanner = stepwisePlanner;
        }

        public async Task ExecuteAsync()
        {
            var sortedTaskIds = _graph.TopologicalSort();

            var paralleTasks = sortedTaskIds.FindAll(taskId => _graph[taskId].Indegress == 0).Select(async taskId =>
            {
                var subTask = _subTasks.FirstOrDefault(x => x.Id == taskId);
                await ExecuteSubTask(_query, subTask);
            })
            .ToList();
            await Task.WhenAll(paralleTasks);

            var serialTasks = sortedTaskIds.FindAll(taskId => _graph[taskId].Indegress > 0).OrderBy(taskId => taskId).ToList();
            foreach (var taskId in serialTasks)
            {
                var subTask = _subTasks.FirstOrDefault(x => x.Id == taskId);
                await ExecuteSubTask(_query, subTask);
            }
        }

        private async Task ExecuteSubTask(string query, SubTask subTask)
        {
            var plan = subTask.AvailableTools.Any()
                ? await _stepwisePlanner.CreatePlanAsync(null, subTask.AvailableTools)
                : await _stepwisePlanner.CreatePlanAsync();

            subTask.Status = Common.Models.Planners.TaskStatus.InProgress;
            OnStepChanged?.Invoke(subTask.AsStepTrace());

            if (!subTask.DependsOn.Any())
            {
                var chatHistory = new ChatHistory();
                chatHistory.AddAssistantMessage($"""
                    [OBSERVATION]
                    用户请求：{query}
                    当前任务: {subTask.Description}
                    """
                );

                var result = await plan.ExecuteAsync(subTask.Description, chatHistory);
                subTask.ExecuteResult = result;
                subTask.Status = string.IsNullOrEmpty(result) ? Common.Models.Planners.TaskStatus.Failed : Common.Models.Planners.TaskStatus.Success;
            }
            else
            {
                var dependencies = _subTasks.FindAll(x => subTask.DependsOn.Contains(x.Id));
                if (dependencies.All(x => x.Status == Common.Models.Planners.TaskStatus.Success))
                {
                    var chatHistory = new ChatHistory();
                    chatHistory.AddAssistantMessage($"""
                        [OBSERVATION]
                        用户请求：{query}
                        当前任务：{subTask.Description}
                        依赖项：
                        {JsonConvert.SerializeObject(dependencies)}
                        """
                    );

                    var result = await plan.ExecuteAsync(subTask.Description, chatHistory);
                    subTask.ExecuteResult = result;
                    subTask.Status = string.IsNullOrEmpty(result) ? Common.Models.Planners.TaskStatus.Failed : Common.Models.Planners.TaskStatus.Success;
                }
            }

            OnStepChanged?.Invoke(subTask.AsStepTrace());
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
            get => _nodes [key];
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
