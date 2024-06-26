namespace PostgreSQL.Embedding.Planners
{
    public interface IStepwisePlanner
    {
        Task<StepwisePlan> CreatePlanAsync(string goal);
    }
}
