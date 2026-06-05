using System.Collections.Concurrent;

namespace InsightaAI.Agent.Models;

/// <summary>
/// 文件读取状态 - 跟踪文件的读取历史和内容
/// 用于 edit_file 工具验证文件是否被修改过
/// </summary>
public class FileReadState
{
    private readonly ConcurrentDictionary<string, FileReadInfo> _readFiles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 记录文件已读取
    /// </summary>
    public void RecordRead(string filePath, string content, DateTime lastModified)
    {
        var absolutePath = Path.GetFullPath(filePath);
        _readFiles[absolutePath] = new FileReadInfo
        {
            Content = content,
            ReadAt = DateTime.UtcNow,
            FileLastModified = lastModified
        };
    }

    /// <summary>
    /// 获取文件读取信息
    /// </summary>
    public FileReadInfo? GetReadInfo(string filePath)
    {
        var absolutePath = Path.GetFullPath(filePath);
        _readFiles.TryGetValue(absolutePath, out var info);
        return info;
    }

    /// <summary>
    /// 检查文件是否已读取
    /// </summary>
    public bool IsFileRead(string filePath)
    {
        var absolutePath = Path.GetFullPath(filePath);
        return _readFiles.ContainsKey(absolutePath);
    }

    /// <summary>
    /// 检查文件自读取后是否被修改
    /// </summary>
    public bool IsFileModifiedSinceRead(string filePath, DateTime currentLastModified)
    {
        var info = GetReadInfo(filePath);
        if (info == null) return true; // 未读取过，视为已修改

        // 比较文件修改时间（忽略毫秒级差异）
        return Math.Abs((currentLastModified - info.FileLastModified).TotalMilliseconds) > 1000;
    }

    /// <summary>
    /// 清除文件读取记录
    /// </summary>
    public void Clear(string? filePath = null)
    {
        if (filePath == null)
        {
            _readFiles.Clear();
        }
        else
        {
            var absolutePath = Path.GetFullPath(filePath);
            _readFiles.TryRemove(absolutePath, out _);
        }
    }
}

/// <summary>
/// 文件读取信息
/// </summary>
public class FileReadInfo
{
    /// <summary>
    /// 文件内容（读取时的完整内容）
    /// </summary>
    public string Content { get; init; } = "";

    /// <summary>
    /// 读取时间
    /// </summary>
    public DateTime ReadAt { get; init; }

    /// <summary>
    /// 文件最后修改时间（读取时）
    /// </summary>
    public DateTime FileLastModified { get; init; }
}
