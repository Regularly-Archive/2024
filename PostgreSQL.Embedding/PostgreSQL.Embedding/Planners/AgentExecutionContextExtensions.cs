namespace PostgreSQL.Embedding.Planners
{
    public static class AgentExecutionContextExtensions
    {
        public static void SetReferenceMessageId(long value)
        {
            AgentExecutionContext.SetGlobalData("ReferenceMessageId", value);
        }

        public static long GetReferenceMessageId() => AgentExecutionContext.GetData<long>("ReferenceMessageId");

        public static void SetMessageId(long value)
        {
            AgentExecutionContext.SetGlobalData("MessageId", value);
        }

        public static long GetMessageId() => AgentExecutionContext.GetData<long>("MessageId");

        public static void SetConversationId(string value)
        {
            AgentExecutionContext.SetGlobalData("ConversationId", value);
        }

        public static string GetConversationId() => AgentExecutionContext.GetData<string>("ConversationId");

        public static void SetAppId(long value)
        {
            AgentExecutionContext.SetGlobalData("AppId", value);
        }

        public static long GetAppId() => AgentExecutionContext.GetData<long>("AppId");

        public static void SetStepId(string value)
        {
            AgentExecutionContext.SetData("StepId", value);
        }

        public static string GetStepId() => AgentExecutionContext.GetData<string>("StepId");
    }
}
