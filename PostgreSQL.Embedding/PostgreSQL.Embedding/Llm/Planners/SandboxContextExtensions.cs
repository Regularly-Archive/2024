using Google.Protobuf.WellKnownTypes;
using LLama.Batched;
using System.IO;
using System.Runtime.CompilerServices;

namespace PostgreSQL.Embedding.Llm.Planners;

public static class SandboxContextExtensions
{
    private const string SandboxContextKey = "_sandboxContext";

    public static void InitializeSandboxContext(
        this AgentExecutionContext ctx,
        long appId,
        string conversationId,
        string runId)
    {
        var sandboxContext = new SandboxContext(appId, conversationId, runId);
        ctx.SetData(SandboxContextKey, sandboxContext);
    }

    public static ISandboxContext GetSandboxContext(this AgentExecutionContext ctx)
    {
        var sandboxContext = ctx.GetData<ISandboxContext>(SandboxContextKey)!;
        if (sandboxContext == null)
        {
            sandboxContext = new SandboxContext(ctx.GetAppId(), ctx.GetConversationId(), ctx.GetRunId());
            ctx.SetData(SandboxContextKey, sandboxContext);
        }

        return sandboxContext;
    }

    public static bool HasSandboxContext(this AgentExecutionContext ctx)
    {
        return ctx.GetData<ISandboxContext>(SandboxContextKey) != null;
    }
}
