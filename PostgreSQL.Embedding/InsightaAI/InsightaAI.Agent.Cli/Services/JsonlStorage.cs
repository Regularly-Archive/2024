using System.Text.Json;
using InsightaAI.Agent.Cli.Models;

namespace InsightaAI.Agent.Cli.Services;

/// <summary>
/// JSONL 格式的会话存储
/// </summary>
public class JsonlStorage
{
    private readonly string _sessionDir;

    public JsonlStorage(string? sessionId = null)
    {
        CliConfig.EnsureSessionsDir();
        sessionId ??= DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _sessionDir = Path.Combine(CliConfig.SessionsDir, sessionId);

        if (!Directory.Exists(_sessionDir))
        {
            Directory.CreateDirectory(_sessionDir);
        }
    }

    /// <summary>
    /// 当前会话 ID
    /// </summary>
    public string SessionId => Path.GetFileName(_sessionDir);

    /// <summary>
    /// 消息文件路径
    /// </summary>
    private string MessagesPath => Path.Combine(_sessionDir, "messages.jsonl");

    /// <summary>
    /// 会话信息文件路径
    /// </summary>
    private string InfoPath => Path.Combine(_sessionDir, "info.json");

    /// <summary>
    /// 保存会话信息
    /// </summary>
    public void SaveSessionInfo(SessionInfo info)
    {
        var json = JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(InfoPath, json);
    }

    /// <summary>
    /// 加载会话信息
    /// </summary>
    public SessionInfo? LoadSessionInfo()
    {
        if (!File.Exists(InfoPath)) return null;
        var json = File.ReadAllText(InfoPath);
        return JsonSerializer.Deserialize<SessionInfo>(json);
    }

    /// <summary>
    /// 追加消息
    /// </summary>
    public void AppendMessage(SessionMessage message)
    {
        var json = JsonSerializer.Serialize(message);
        File.AppendAllText(MessagesPath, json + Environment.NewLine);
    }

    /// <summary>
    /// 加载所有消息
    /// </summary>
    public List<SessionMessage> LoadMessages()
    {
        if (!File.Exists(MessagesPath))
            return new List<SessionMessage>();

        var lines = File.ReadAllLines(MessagesPath);
        return lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => JsonSerializer.Deserialize<SessionMessage>(l))
            .Where(m => m != null)
            .Cast<SessionMessage>()
            .ToList();
    }

    /// <summary>
    /// 获取所有会话
    /// </summary>
    public static List<SessionInfo> GetAllSessions()
    {
        CliConfig.EnsureSessionsDir();

        return Directory.GetDirectories(CliConfig.SessionsDir)
            .Select(dir =>
            {
                var infoPath = Path.Combine(dir, "info.json");
                if (File.Exists(infoPath))
                {
                    try
                    {
                        var json = File.ReadAllText(infoPath);
                        return JsonSerializer.Deserialize<SessionInfo>(json);
                    }
                    catch
                    {
                        return null;
                    }
                }
                return null;
            })
            .Where(s => s != null)
            .Cast<SessionInfo>()
            .OrderByDescending(s => s.CreatedAt)
            .ToList();
    }
}
