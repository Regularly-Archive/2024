using System.Text.Json;
using System.Text;
using Microsoft.Data.Sqlite;

namespace InsightaAI.Agent.Memory;

/// <summary>
/// 基于 SQLite 和 FTS5 的本地记忆 Provider。
/// SQLite 是记忆数据与全文索引的唯一事实源；FTS 只负责候选召回，
/// 用户、项目和类型等边界始终由主表过滤。
/// </summary>
public sealed class SqliteMemoryProvider : IMemoryProvider, IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initializationLock = new(1, 1);
    private volatile bool _initialized;

    public SqliteMemoryProvider(string? databasePath = null)
    {
        databasePath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".insighta",
            "memories",
            "memories.db");

        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task SaveMemoryAsync(MemoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Id);
        if (entry.Scope == MemoryScope.Private)
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.UserId);
        if (entry.Scope == MemoryScope.Team)
            ArgumentException.ThrowIfNullOrWhiteSpace(entry.Project);

        if (entry.CreatedAt == default)
            entry.CreatedAt = DateTime.UtcNow;
        if (entry.UpdatedAt == default)
            entry.UpdatedAt = entry.CreatedAt;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO memory_entries (
                id, user_id, name, description, content, type, scope, activation, tags_json, source, project,
                created_at, updated_at, last_accessed_at, access_count)
            VALUES (
                $id, $userId, $name, $description, $content, $type, $scope, $activation, $tagsJson, $source, $project,
                $createdAt, $updatedAt, $lastAccessedAt, $accessCount)
            ON CONFLICT(id) DO UPDATE SET
                user_id = excluded.user_id,
                name = excluded.name,
                description = excluded.description,
                content = excluded.content,
                type = excluded.type,
                scope = excluded.scope,
                activation = excluded.activation,
                tags_json = excluded.tags_json,
                source = excluded.source,
                project = excluded.project,
                updated_at = excluded.updated_at,
                last_accessed_at = excluded.last_accessed_at,
                access_count = excluded.access_count;
            """;
        AddMemoryParameters(command, entry);
        await command.ExecuteNonQueryAsync(cancellationToken);

        await ReplaceFtsEntryAsync(connection, transaction, entry, cancellationToken);
        transaction.Commit();
    }

    public async Task<MemoryEntry?> GetMemoryAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = $"SELECT {MemoryColumns} FROM memory_entries m WHERE m.id = $id;";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadMemory(reader) : null;
    }

    public async Task<List<MemoryEntry>> SearchMemoriesAsync(
        string userId,
        string query,
        MemorySearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        options ??= new MemorySearchOptions();

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        var scopeFilter = "(m.scope = $privateScope AND m.user_id = $userId)" +
            (string.IsNullOrWhiteSpace(options.ProjectId)
                ? string.Empty
                : " OR (m.scope = $teamScope AND m.project = $projectId)");
        var typeFilter = options.Type.HasValue ? " AND m.type = $type" : string.Empty;
        var tagFilter = options.Tags is { Count: > 0 }
            ? " AND " + string.Join(" AND ", options.Tags.Select((_, index) =>
                $"EXISTS (SELECT 1 FROM json_each(m.tags_json) WHERE lower(value) = lower($tag{index}))"))
            : string.Empty;

        // FTS5 trigram cannot match a full-text query shorter than three Unicode characters.
        // For short terms (IDs, tags and error codes), use an exact substring fallback.
        var isShortQuery = query.EnumerateRunes().Count() < 3;
        command.CommandText = isShortQuery
            ? $"""
                SELECT {MemoryColumns}, 1.0 AS relevance_score
                FROM memory_entries m
                WHERE ({scopeFilter}){typeFilter}{tagFilter}
                  AND (m.name LIKE $likeQuery ESCAPE '^'
                       OR m.description LIKE $likeQuery ESCAPE '^'
                       OR m.content LIKE $likeQuery ESCAPE '^'
                       OR m.tags_json LIKE $likeQuery ESCAPE '^')
                ORDER BY m.updated_at DESC
                LIMIT $maxResults;
                """
            : $"""
                SELECT {MemoryColumns}, -bm25(memory_fts) AS relevance_score
                FROM memory_fts
                JOIN memory_entries m ON m.id = memory_fts.memory_id
                WHERE memory_fts MATCH $ftsQuery
                  AND ({scopeFilter}){typeFilter}{tagFilter}
                ORDER BY bm25(memory_fts)
                LIMIT $maxResults;
                """;

        command.Parameters.AddWithValue("$privateScope", MemoryScope.Private.ToString());
        command.Parameters.AddWithValue("$teamScope", MemoryScope.Team.ToString());
        command.Parameters.AddWithValue("$userId", userId);
        if (!string.IsNullOrWhiteSpace(options.ProjectId))
            command.Parameters.AddWithValue("$projectId", options.ProjectId);
        if (options.Type.HasValue)
            command.Parameters.AddWithValue("$type", options.Type.Value.ToString());
        if (options.Tags is { Count: > 0 } tags)
        {
            for (var index = 0; index < tags.Count; index++)
                command.Parameters.AddWithValue($"$tag{index}", tags[index]);
        }
        command.Parameters.AddWithValue("$maxResults", options.MaxResults);

        if (isShortQuery)
            command.Parameters.AddWithValue("$likeQuery", $"%{EscapeLike(query)}%");
        else
            command.Parameters.AddWithValue("$ftsQuery", BuildFtsQuery(query));

        var results = new List<MemoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var memory = ReadMemory(reader);
            memory.RelevanceScore = NormalizeBm25Score(reader.GetFloat(reader.GetOrdinal("relevance_score")));
            results.Add(memory);
        }

        if (results.Count == 0 && !isShortQuery)
        {
            var exactMatches = await SearchExactTermsAsync(connection, userId, query, options, cancellationToken);
            if (exactMatches.Count > 0)
                return exactMatches;
        }

        // A caller that explicitly requests one memory type has supplied a strong intent.
        // Without embeddings, broad questions such as "who am I?" may share no literal text
        // with a User memory. Return the most recently updated entries of that type instead
        // of incorrectly reporting that no such memories exist.
        if (results.Count == 0 && options.Type.HasValue && string.IsNullOrWhiteSpace(options.ProjectId))
            return await ListMemoriesAsync(userId, options.Type, 0, options.MaxResults, cancellationToken);

        return results;
    }

    public async Task<List<MemoryEntry>> ListMemoriesAsync(
        string userId,
        MemoryType? type = null,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {MemoryColumns}
            FROM memory_entries m
            WHERE user_id = $userId AND scope = $scope
            {(type.HasValue ? "AND type = $type" : string.Empty)}
            ORDER BY updated_at DESC
            LIMIT $take OFFSET $skip;
            """;
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$scope", MemoryScope.Private.ToString());
        command.Parameters.AddWithValue("$take", take);
        command.Parameters.AddWithValue("$skip", skip);
        if (type.HasValue)
            command.Parameters.AddWithValue("$type", type.Value.ToString());

        var results = new List<MemoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(ReadMemory(reader));
        return results;
    }

    public async Task<List<MemoryEntry>> ListCoreMemoriesAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT {MemoryColumns}
            FROM memory_entries m
            WHERE m.user_id = $userId
              AND m.scope = $scope
              AND m.activation = $activation
            ORDER BY m.updated_at DESC;
            """;
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$scope", MemoryScope.Private.ToString());
        command.Parameters.AddWithValue("$activation", MemoryActivation.Core.ToString());

        var results = new List<MemoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            results.Add(ReadMemory(reader));
        return results;
    }

    public async Task UpdateMemoryAsync(MemoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        entry.UpdatedAt = DateTime.UtcNow;
        await SaveMemoryAsync(entry, cancellationToken);
    }

    public async Task DeleteMemoryAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        await ExecuteAsync(connection, transaction,
            "DELETE FROM memory_fts WHERE memory_id = $id;", cancellationToken, ("$id", id));
        await ExecuteAsync(connection, transaction,
            "DELETE FROM memory_entries WHERE id = $id;", cancellationToken, ("$id", id));
        transaction.Commit();
    }

    public async Task TouchMemoryAsync(string id, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE memory_entries
            SET last_accessed_at = $accessedAt, access_count = access_count + 1
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$accessedAt", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string> GetMemoryIndexAsync(
        string userId,
        string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var privateCount = await CountMemoriesAsync(connection,
            "scope = $scope AND user_id = $userId", cancellationToken,
            ("$scope", MemoryScope.Private.ToString()), ("$userId", userId));
        var teamCount = string.IsNullOrWhiteSpace(projectId)
            ? 0
            : await CountMemoriesAsync(connection,
                "scope = $scope AND project = $projectId", cancellationToken,
                ("$scope", MemoryScope.Team.ToString()), ("$projectId", projectId));

        if (privateCount == 0 && teamCount == 0)
            return string.Empty;

        return $"You have {privateCount} private memories and {teamCount} team memories available. " +
               "Use search_memory to retrieve information relevant to the current task.";
    }

    public async Task<UserProfile?> GetUserProfileAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = "SELECT profile_json FROM user_profiles WHERE user_id = $userId;";
        command.Parameters.AddWithValue("$userId", userId);
        var json = await command.ExecuteScalarAsync(cancellationToken) as string;
        return json is null ? null : JsonSerializer.Deserialize<UserProfile>(json);
    }

    public async Task SaveUserProfileAsync(UserProfile profile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.UserId);
        profile.LastUpdated = DateTime.UtcNow;

        await using var connection = await OpenConnectionAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO user_profiles (user_id, profile_json, updated_at)
            VALUES ($userId, $profileJson, $updatedAt)
            ON CONFLICT(user_id) DO UPDATE SET
                profile_json = excluded.profile_json,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$userId", profile.UserId);
        command.Parameters.AddWithValue("$profileJson", JsonSerializer.Serialize(profile));
        command.Parameters.AddWithValue("$updatedAt", profile.LastUpdated.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _initializationLock.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken);
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        await _initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
                return;

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA foreign_keys = ON;

                CREATE TABLE IF NOT EXISTS memory_entries (
                    id TEXT PRIMARY KEY,
                    user_id TEXT NOT NULL,
                    name TEXT NOT NULL,
                    description TEXT NOT NULL,
                    content TEXT NOT NULL,
                    type TEXT NOT NULL,
                    scope TEXT NOT NULL,
                    activation TEXT NOT NULL DEFAULT 'OnDemand',
                    tags_json TEXT NOT NULL,
                    source TEXT NOT NULL,
                    project TEXT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL,
                    last_accessed_at TEXT NULL,
                    access_count INTEGER NOT NULL DEFAULT 0
                );

                CREATE TABLE IF NOT EXISTS user_profiles (
                    user_id TEXT PRIMARY KEY,
                    profile_json TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_memory_private
                    ON memory_entries (user_id, scope, updated_at DESC);
                CREATE INDEX IF NOT EXISTS idx_memory_team
                    ON memory_entries (project, scope, updated_at DESC);

                CREATE VIRTUAL TABLE IF NOT EXISTS memory_fts USING fts5(
                    memory_id UNINDEXED,
                    name,
                    description,
                    content,
                    tags,
                    tokenize = 'trigram'
                );
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            await EnsureColumnAsync(connection, "memory_entries", "activation",
                "TEXT NOT NULL DEFAULT 'OnDemand'", cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initializationLock.Release();
        }
    }

    private static async Task ReplaceFtsEntryAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        MemoryEntry entry,
        CancellationToken cancellationToken)
    {
        await ExecuteAsync(connection, transaction,
            "DELETE FROM memory_fts WHERE memory_id = $id;", cancellationToken, ("$id", entry.Id));
        await ExecuteAsync(connection, transaction, """
            INSERT INTO memory_fts (memory_id, name, description, content, tags)
            VALUES ($id, $name, $description, $content, $tags);
            """, cancellationToken,
            ("$id", entry.Id),
            ("$name", entry.Name),
            ("$description", entry.Description),
            ("$content", entry.Content),
            ("$tags", string.Join(' ', entry.Tags)));
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string table,
        string column,
        string definition,
        CancellationToken cancellationToken)
    {
        var check = connection.CreateCommand();
        await using var disposableCheck = check;
        check.CommandText = $"SELECT 1 FROM pragma_table_info('{table}') WHERE name = $column LIMIT 1;";
        check.Parameters.AddWithValue("$column", column);
        if (await check.ExecuteScalarAsync(cancellationToken) is not null)
            return;

        var alter = connection.CreateCommand();
        await using var disposableAlter = alter;
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        await alter.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        await using var disposableCommand = command;
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> CountMemoriesAsync(
        SqliteConnection connection,
        string filter,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();
        await using var disposableCommand = command;
        command.CommandText = $"SELECT COUNT(*) FROM memory_entries WHERE {filter};";
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static void AddMemoryParameters(SqliteCommand command, MemoryEntry entry)
    {
        command.Parameters.AddWithValue("$id", entry.Id);
        command.Parameters.AddWithValue("$userId", entry.UserId);
        command.Parameters.AddWithValue("$name", entry.Name);
        command.Parameters.AddWithValue("$description", entry.Description);
        command.Parameters.AddWithValue("$content", entry.Content);
        command.Parameters.AddWithValue("$type", entry.Type.ToString());
        command.Parameters.AddWithValue("$scope", entry.Scope.ToString());
        command.Parameters.AddWithValue("$activation", entry.Activation.ToString());
        command.Parameters.AddWithValue("$tagsJson", JsonSerializer.Serialize(entry.Tags));
        command.Parameters.AddWithValue("$source", entry.Source);
        command.Parameters.AddWithValue("$project", entry.Project ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", entry.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", entry.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$lastAccessedAt", entry.LastAccessedAt?.ToString("O") ?? (object)DBNull.Value);
        command.Parameters.AddWithValue("$accessCount", entry.AccessCount);
    }

    private static MemoryEntry ReadMemory(SqliteDataReader reader)
    {
        return new MemoryEntry
        {
            Id = reader.GetString(reader.GetOrdinal("id")),
            UserId = reader.GetString(reader.GetOrdinal("user_id")),
            Name = reader.GetString(reader.GetOrdinal("name")),
            Description = reader.GetString(reader.GetOrdinal("description")),
            Content = reader.GetString(reader.GetOrdinal("content")),
            Type = Enum.Parse<MemoryType>(reader.GetString(reader.GetOrdinal("type"))),
            Scope = Enum.Parse<MemoryScope>(reader.GetString(reader.GetOrdinal("scope"))),
            Activation = Enum.Parse<MemoryActivation>(reader.GetString(reader.GetOrdinal("activation"))),
            Tags = JsonSerializer.Deserialize<List<string>>(reader.GetString(reader.GetOrdinal("tags_json"))) ?? [],
            Source = reader.GetString(reader.GetOrdinal("source")),
            Project = reader.IsDBNull(reader.GetOrdinal("project")) ? null : reader.GetString(reader.GetOrdinal("project")),
            CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
            UpdatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("updated_at"))),
            LastAccessedAt = reader.IsDBNull(reader.GetOrdinal("last_accessed_at"))
                ? null
                : DateTime.Parse(reader.GetString(reader.GetOrdinal("last_accessed_at"))),
            AccessCount = reader.GetInt32(reader.GetOrdinal("access_count"))
        };
    }

    private async Task<List<MemoryEntry>> SearchExactTermsAsync(
        SqliteConnection connection,
        string userId,
        string query,
        MemorySearchOptions options,
        CancellationToken cancellationToken)
    {
        var terms = ExtractExactTerms(query);
        if (terms.Count == 0)
            return [];

        var command = connection.CreateCommand();
        var scopeFilter = "(m.scope = $privateScope AND m.user_id = $userId)" +
            (string.IsNullOrWhiteSpace(options.ProjectId) ? string.Empty : " OR (m.scope = $teamScope AND m.project = $projectId)");
        var typeFilter = options.Type.HasValue ? " AND m.type = $type" : string.Empty;
        var matches = string.Join(" OR ", terms.Select((_, index) =>
            $"m.name LIKE $term{index} ESCAPE '^' OR m.description LIKE $term{index} ESCAPE '^' OR m.content LIKE $term{index} ESCAPE '^' OR m.tags_json LIKE $term{index} ESCAPE '^'"));
        command.CommandText = $"SELECT {MemoryColumns}, 0.1 AS relevance_score FROM memory_entries m WHERE ({scopeFilter}){typeFilter} AND ({matches}) ORDER BY m.updated_at DESC LIMIT $maxResults;";
        command.Parameters.AddWithValue("$privateScope", MemoryScope.Private.ToString());
        command.Parameters.AddWithValue("$teamScope", MemoryScope.Team.ToString());
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$maxResults", options.MaxResults);
        if (!string.IsNullOrWhiteSpace(options.ProjectId)) command.Parameters.AddWithValue("$projectId", options.ProjectId);
        if (options.Type.HasValue) command.Parameters.AddWithValue("$type", options.Type.Value.ToString());
        for (var index = 0; index < terms.Count; index++) command.Parameters.AddWithValue($"$term{index}", $"%{EscapeLike(terms[index])}%");

        var results = new List<MemoryEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var memory = ReadMemory(reader);
            memory.RelevanceScore = reader.GetFloat(reader.GetOrdinal("relevance_score"));
            results.Add(memory);
        }
        return results;
    }

    private const string MemoryColumns = """
        m.id, m.user_id, m.name, m.description, m.content, m.type, m.scope, m.activation, m.tags_json,
        m.source, m.project, m.created_at, m.updated_at, m.last_accessed_at, m.access_count
        """;

    private static string BuildFtsQuery(string value)
    {
        var trigrams = value.EnumerateRunes()
            .Where(rune => !Rune.IsWhiteSpace(rune))
            .Select(rune => rune.ToString())
            .Aggregate("", (text, rune) => text + rune)
            .EnumerateRunes()
            .Chunk(3)
            .Where(chunk => chunk.Length == 3)
            .Select(chunk => string.Concat(chunk))
            .Distinct(StringComparer.Ordinal)
            .Take(16)
            .Select(term => $"\"{term.Replace("\"", "\"\"")}\"");
        return string.Join(" OR ", trigrams);
    }

    private static List<string> ExtractExactTerms(string value)
    {
        var terms = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (terms.Count == 1 && terms[0].EnumerateRunes().Count() > 2)
        {
            terms.AddRange(terms[0].EnumerateRunes()
                .Chunk(2)
                .Where(chunk => chunk.Length == 2)
                .Select(chunk => string.Concat(chunk)));
        }
        return terms.Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToList();
    }

    // FTS5 BM25 is an unbounded ranking value, not a probability. Preserve its ordering
    // while exposing a stable 0..1 score to tools and callers.
    private static float NormalizeBm25Score(float score) => score <= 0
        ? 0
        : Math.Clamp(score / (1 + score), 0, 1);

    private static string EscapeLike(string value) => value
        .Replace("^", "^^", StringComparison.Ordinal)
        .Replace("%", "^%", StringComparison.Ordinal)
        .Replace("_", "^_", StringComparison.Ordinal);
}
