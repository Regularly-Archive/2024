using PostgreSQL.Embedding.Domain.Models.RAG;
using System.Collections.Concurrent;

namespace PostgreSQL.Embedding.Llm.Planners
{
    public static class AgentExecutionContextExtensions
    {
        private const string CitationsKey = "G_Citations";

        public static void SetReferenceMessageId(this AgentExecutionContext context, long value)
        {
            context.SetGlobalData("G_ReferenceMessageId", value);
        }

        public static long GetReferenceMessageId(this AgentExecutionContext context) => context.GetData<long>("G_ReferenceMessageId");

        public static void SetMessageId(this AgentExecutionContext context, long value)
        {
            context.SetGlobalData("G_MessageId", value);
        }

        public static long GetMessageId(this AgentExecutionContext context) => context.GetData<long>("G_MessageId");

        public static void SetConversationId(this AgentExecutionContext context, string value)
        {
            context.SetGlobalData("G_ConversationId", value);
        }

        public static string GetConversationId(this AgentExecutionContext context) => context.GetData<string>("G_ConversationId");

        public static void SetAppId(this AgentExecutionContext context, long value)
        {
            context.SetGlobalData("G_AppId", value);
        }

        public static long GetAppId(this AgentExecutionContext context) => context.GetData<long>("G_AppId");

        public static void SetStepId(this AgentExecutionContext context, string value)
        {
            context.SetData("L_StepId", value);
        }

        public static string GetStepId(this AgentExecutionContext context) => context.GetData<string>("L_StepId");

        public static void SetRunId(this AgentExecutionContext context, string value)
        {
            context.SetData("L_RunId", value);
        }

        public static string GetRunId(this AgentExecutionContext context) => context.GetData<string>("L_RunId");

        /// <summary>
        /// Add citations to the context (thread-safe, for multi-phase RAG)
        /// </summary>
        public static void AddCitations(this AgentExecutionContext context, List<LlmCitationModel> citations)
        {
            var bag = context.GetGlobalData<ConcurrentBag<LlmCitationModel>>(CitationsKey);
            if (bag == null) bag = new ConcurrentBag<LlmCitationModel>();

            foreach (var citation in citations)
            {
                bag.Add(citation);
            }

            context.SetGlobalData(CitationsKey, bag);
        }

        /// <summary>
        /// Get all citations collected from multiple RAG phases
        /// </summary>
        public static List<LlmCitationModel> GetCitations(this AgentExecutionContext context)
        {
            var bag = context.GetGlobalData<ConcurrentBag<LlmCitationModel>>(CitationsKey);
            return bag?.ToList() ?? new List<LlmCitationModel>();
        }

        /// <summary>
        /// Clear all citations (useful for new conversations)
        /// </summary>
        public static void ClearCitations(this AgentExecutionContext context)
        {
            context.SetGlobalData(CitationsKey, new ConcurrentBag<LlmCitationModel>());
        }
    }
}
