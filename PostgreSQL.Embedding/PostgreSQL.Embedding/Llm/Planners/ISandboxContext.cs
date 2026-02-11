namespace PostgreSQL.Embedding.Llm.Planners;

public interface ISandboxContext
{
    string BaseDir { get; }
    string AppDir { get; }
    string SessionDir { get; }
    string RunDir { get; }
    string ArtifactsDir { get; }

    string ResolvePath(string relativePath);
    bool IsPathAllowed(string path);

    string ToLinuxStyleRelativePath(string basePath, string fullPath);
    string FromLinuxStyleRelativePath(string basePath, string linuxPath);
}
