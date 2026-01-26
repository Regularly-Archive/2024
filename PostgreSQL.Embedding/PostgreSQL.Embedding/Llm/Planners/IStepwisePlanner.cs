namespace PostgreSQL.Embedding.Llm.Planners
{
    public interface IStepwisePlanner
    {
        Task<StepwisePlan> CreatePlanAsync();
    }
}
