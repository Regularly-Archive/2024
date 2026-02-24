namespace PostgreSQL.Embedding.Llm.Planners;

public interface ISandboxContext
{
    string BaseDir { get; }
    string AppDir { get; }
    string SessionDir { get; }
    string RunDir { get; }
    string ArtifactsDir { get; }

    string ToLocalPath(string sandboxPath);
    bool IsPathAllowed(string path);

    string ToSandboxPath(string localFullPath);
    Dictionary<string, string> GetVolumeMappings();
}
