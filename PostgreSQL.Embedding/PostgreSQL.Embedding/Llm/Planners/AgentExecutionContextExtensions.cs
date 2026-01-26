namespace PostgreSQL.Embedding.Llm.Planners
{
    public static class AgentExecutionContextExtensions
    {
        public static void SetReferenceMessageId(this AgentExecutionContext context, long value)
        {
            context.SetGlobalData("ReferenceMessageId", value);
        }

        public static long GetReferenceMessageId(this AgentExecutionContext context) => context.GetData<long>("ReferenceMessageId");

        public static void SetMessageId(this AgentExecutionContext context, long value)
        {
            context.SetGlobalData("MessageId", value);
        }

        public static long GetMessageId(this AgentExecutionContext context) => context.GetData<long>("MessageId");

        public static void SetConversationId(this AgentExecutionContext context, string value)
        {
            context.SetGlobalData("ConversationId", value);
        }

        public static string GetConversationId(this AgentExecutionContext context) => context.GetData<string>("ConversationId");

        public static void SetAppId(this AgentExecutionContext context, long value)
        {
            context.SetGlobalData("AppId", value);
        }

        public static long GetAppId(this AgentExecutionContext context) => context.GetData<long>("AppId");

        public static void SetStepId(this AgentExecutionContext context, string value)
        {
            context.SetData("StepId", value);
        }

        public static string GetStepId(this AgentExecutionContext context) => context.GetData<string>("StepId");

        public static void SetRunId(this AgentExecutionContext context, string value)
        {
            context.SetData("RunId", value);
        }

        public static string GetRunId(this AgentExecutionContext context) => context.GetData<string>("RunId");
    }
}
