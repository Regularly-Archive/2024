namespace PostgreSQL.Embedding.Planners
{
    public class StepwisePlannerConfig
    {
        public string Suffix { get; set; } = @"Let's break down the problem step by step and think about the best approach. Label steps as they are taken. Continue the thought process!";

        public int MaxIterations { get; set; } = 15;

        public int MinIterationTimeMs { get; set; } = 2000;

        public List<string> ExcludedPlugins { get; set; } = new List<string>();

        public List<string> ExcludedFunctions { get; set; } = new List<string>();
    }
}
