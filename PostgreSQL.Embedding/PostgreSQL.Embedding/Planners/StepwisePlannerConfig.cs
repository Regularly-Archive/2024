using PostgreSQL.Embedding.Plugins;

namespace PostgreSQL.Embedding.Planners
{
    public class StepwisePlannerConfig
    {
        public string Suffix { get; set; } = @"Let's break down the problem step by step and think about the best approach. Label steps as they are taken. Continue the thought process!";

        public int MaxIterations { get; set; } = 15;

        public int MinIterationTimeMs { get; set; } = 2000;

        public List<string> ExcludedPlugins { get; set; } = new List<string>() { 
            nameof(BingSearchPlugin), 
            nameof(BraveSearchPlugin)
        };

        public List<string> ExcludedFunctions { get; set; } = new List<string>()
        {
            "BingSearchPlugin.Search",
            "BraveSearchPlugin.Search",
            "JinaAIPlugin.Search"
        };

        public Dictionary<string, object> Variables { get; set; } = new Dictionary<string, object>();
    }
}
