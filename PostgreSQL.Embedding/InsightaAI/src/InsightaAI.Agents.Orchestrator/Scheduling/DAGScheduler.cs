using InsightaAI.Agents.Orchestrator.Nodes;
using InsightaAI.Agents.Orchestrator.Results;

namespace InsightaAI.Agents.Orchestrator.Scheduling;

/// <summary>
/// DAG 调度器 - 拓扑排序 + 并行批次调度
/// 基于现有的 DAGraphExecutor 模式但更通用
/// </summary>
public sealed class DAGScheduler
{
    private readonly Dictionary<string, DAGNode> _nodeMap;
    private readonly Dictionary<string, NodeState> _states;
    private readonly Dictionary<string, object?> _results;
    private readonly Dictionary<string, Exception?> _errors;

    public DAGScheduler(DAGNode[] nodes)
    {
        _nodeMap = nodes.ToDictionary(n => n.Id);
        _states = nodes.ToDictionary(n => n.Id, _ => NodeState.Pending);
        _results = new Dictionary<string, object?>();
        _errors = new Dictionary<string, Exception?>();

        // 验证：检查重复 ID
        if (nodes.Length != _nodeMap.Count)
            throw new ArgumentException("Duplicate node IDs detected");

        // 验证：检查依赖引用是否存在
        foreach (var node in nodes)
        {
            foreach (var dep in node.DependsOn)
            {
                if (!_nodeMap.ContainsKey(dep))
                    throw new ArgumentException($"Node '{node.Id}' depends on non-existent node '{dep}'");
            }
        }
    }

    /// <summary>获取所有就绪的任务（依赖已满足）</summary>
    public DAGNode[] GetReadyTasks()
    {
        return _nodeMap.Values
            .Where(n => _states[n.Id] == NodeState.Pending &&
                       n.DependsOn.All(dep => _states[dep] == NodeState.Completed))
            .ToArray();
    }

    /// <summary>标记节点完成</summary>
    public void MarkComplete(string nodeId, object? result)
    {
        _states[nodeId] = NodeState.Completed;
        _results[nodeId] = result;
    }

    /// <summary>标记节点失败</summary>
    public void MarkFailed(string nodeId, Exception error)
    {
        _states[nodeId] = NodeState.Failed;
        _errors[nodeId] = error;
    }

    /// <summary>标记节点跳过</summary>
    public void MarkSkipped(string nodeId)
    {
        _states[nodeId] = NodeState.Skipped;
    }

    /// <summary>是否所有节点都已完成（终态）</summary>
    public bool IsComplete => _states.Values.All(s =>
        s is NodeState.Completed or NodeState.Failed or NodeState.Skipped);

    /// <summary>是否有失败的节点</summary>
    public bool HasFailures => _states.Values.Any(s => s == NodeState.Failed);

    /// <summary>获取节点结果</summary>
    public object? GetResult(string nodeId)
    {
        return _results.TryGetValue(nodeId, out var result) ? result : null;
    }

    /// <summary>获取节点错误</summary>
    public Exception? GetError(string nodeId)
    {
        return _errors.TryGetValue(nodeId, out var error) ? error : null;
    }

    /// <summary>根据 ID 获取节点定义</summary>
    public DAGNode? GetNode(string nodeId)
    {
        return _nodeMap.TryGetValue(nodeId, out var node) ? node : null;
    }

    /// <summary>获取所有节点结果</summary>
    public NodeResult[] GetAllResults()
    {
        return _nodeMap.Values.Select(node => new NodeResult
        {
            NodeId = node.Id,
            NodeName = node.Name,
            NodeKind = node.Kind,
            Status = _states[node.Id] switch
            {
                NodeState.Completed => NodeResultStatus.Success,
                NodeState.Failed => NodeResultStatus.Failed,
                NodeState.Skipped => NodeResultStatus.Skipped,
                _ => NodeResultStatus.Cancelled
            },
            Output = GetResult(node.Id),
            Error = GetError(node.Id)?.Message
        }).ToArray();
    }

    /// <summary>
    /// 标记下游节点为跳过（当依赖失败时）
    /// </summary>
    public void MarkDownstreamSkipped(string failedNodeId)
    {
        var toSkip = new Queue<string>();
        toSkip.Enqueue(failedNodeId);

        while (toSkip.Count > 0)
        {
            var currentId = toSkip.Dequeue();
            var dependents = _nodeMap.Values
                .Where(n => n.DependsOn.Contains(currentId) && _states[n.Id] == NodeState.Pending);

            foreach (var dependent in dependents)
            {
                _states[dependent.Id] = NodeState.Skipped;
                toSkip.Enqueue(dependent.Id);
            }
        }
    }

    /// <summary>
    /// 验证 DAG：循环检测（Kahn 算法）
    /// 依赖存在性在构造函数中已验证
    /// </summary>
    public ValidationResult Validate()
    {
        try
        {
            TopologicalSort();
            return ValidationResult.Success();
        }
        catch (InvalidOperationException ex)
        {
            return ValidationResult.Failure(ex.Message);
        }
    }

    /// <summary>
    /// 拓扑排序。如果检测到循环则抛出异常。
    /// </summary>
    public string[] TopologicalSort()
    {
        var inDegree = _nodeMap.ToDictionary(kvp => kvp.Key, _ => 0);
        var adjacency = _nodeMap.ToDictionary(kvp => kvp.Key, _ => new List<string>());

        // 构建邻接表和入度表
        foreach (var node in _nodeMap.Values)
        {
            foreach (var dep in node.DependsOn)
            {
                adjacency[dep].Add(node.Id);
                inDegree[node.Id]++;
            }
        }

        // Kahn 算法
        var queue = new Queue<string>();
        foreach (var (id, degree) in inDegree)
        {
            if (degree == 0)
                queue.Enqueue(id);
        }

        var result = new List<string>();
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(current);

            foreach (var neighbor in adjacency[current])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    queue.Enqueue(neighbor);
            }
        }

        if (result.Count != _nodeMap.Count)
            throw new InvalidOperationException("Graph contains a cycle");

        return result.ToArray();
    }

    /// <summary>
    /// 获取并行批次。Batch 0 = 无依赖节点，Batch 1 = 仅依赖 Batch 0 的节点，依此类推。
    /// </summary>
    public DAGNode[][] GetParallelBatches()
    {
        var sorted = TopologicalSort();
        var levels = new Dictionary<string, int>();

        // 计算每个节点的层级
        foreach (var nodeId in sorted)
        {
            var node = _nodeMap[nodeId];
            if (node.DependsOn.Length == 0)
            {
                levels[nodeId] = 0;
            }
            else
            {
                levels[nodeId] = node.DependsOn.Max(dep => levels[dep]) + 1;
            }
        }

        // 按层级分组
        return levels
            .GroupBy(kvp => kvp.Value)
            .OrderBy(g => g.Key)
            .Select(g => g.Select(kvp => _nodeMap[kvp.Key]).ToArray())
            .ToArray();
    }
}
