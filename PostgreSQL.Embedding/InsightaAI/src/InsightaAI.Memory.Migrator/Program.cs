using System.Text.Json;
using InsightaAI.Agent.Memory;
using Microsoft.Data.Sqlite;

return await MemoryMigrationProgram.RunAsync(args);

internal static class MemoryMigrationProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        var options = MigrationOptions.Parse(args);
        if (options.ShowHelp)
        {
            Console.WriteLine(MigrationOptions.HelpText);
            return 0;
        }

        if (options.Inspect)
            return await InspectAsync(options.DatabasePath);

        if (!Directory.Exists(options.SourceDirectory))
        {
            Console.Error.WriteLine($"Source directory does not exist: {options.SourceDirectory}");
            return 2;
        }

        Console.WriteLine($"Source:      {options.SourceDirectory}");
        Console.WriteLine($"Destination: {options.DatabasePath}");
        Console.WriteLine(options.Apply ? "Mode:        apply (upsert)" : "Mode:        dry-run (no data will be written)");

        await using var destination = options.Apply ? new SqliteMemoryProvider(options.DatabasePath) : null;
        var report = new MigrationReport();

        await MigrateMemoriesAsync(
            Path.Combine(options.SourceDirectory, "private"),
            MemoryScope.Private,
            destination,
            options.Apply,
            report);
        await MigrateMemoriesAsync(
            Path.Combine(options.SourceDirectory, "team"),
            MemoryScope.Team,
            destination,
            options.Apply,
            report);
        await MigrateProfilesAsync(
            Path.Combine(options.SourceDirectory, "private"),
            destination,
            options.Apply,
            report);

        Console.WriteLine();
        Console.WriteLine($"Memories: {report.MigratedMemories} migrated, {report.SkippedMemories} skipped");
        Console.WriteLine($"Profiles: {report.MigratedProfiles} migrated, {report.SkippedProfiles} skipped");
        Console.WriteLine($"Errors:   {report.Errors.Count}");
        foreach (var error in report.Errors)
            Console.Error.WriteLine($"  - {error}");

        if (!options.Apply)
            Console.WriteLine("Dry-run complete. Re-run with --apply to write the migrated data.");

        return report.Errors.Count == 0 ? 0 : 1;
    }

    private static async Task MigrateMemoriesAsync(
        string scopesDirectory,
        MemoryScope scope,
        SqliteMemoryProvider? destination,
        bool apply,
        MigrationReport report)
    {
        if (!Directory.Exists(scopesDirectory))
            return;

        foreach (var ownerDirectory in Directory.EnumerateDirectories(scopesDirectory))
        {
            var owner = Path.GetFileName(ownerDirectory);
            var memoriesDirectory = Path.Combine(ownerDirectory, "memories");
            if (!Directory.Exists(memoriesDirectory))
                continue;

            foreach (var filePath in Directory.EnumerateFiles(memoriesDirectory, "*.md"))
            {
                try
                {
                    var markdown = await File.ReadAllTextAsync(filePath);
                    var entry = MemoryMarkdownSerializer.Parse(
                        markdown,
                        Path.GetFileNameWithoutExtension(filePath),
                        owner,
                        scope);
                    if (entry is null)
                    {
                        report.SkippedMemories++;
                        report.Errors.Add($"Invalid memory frontmatter: {filePath}");
                        continue;
                    }

                    // The directory is authoritative for legacy data. Do not allow a stale
                    // frontmatter scope or owner to move a memory across its original boundary.
                    entry.Scope = scope;
                    if (scope == MemoryScope.Private)
                    {
                        entry.UserId = owner;
                        entry.Project = null;
                    }
                    else
                    {
                        entry.UserId = string.Empty;
                        entry.Project = owner;
                    }

                    if (apply)
                        await destination!.SaveMemoryAsync(entry);
                    report.MigratedMemories++;
                }
                catch (Exception exception)
                {
                    report.SkippedMemories++;
                    report.Errors.Add($"{filePath}: {exception.Message}");
                }
            }
        }
    }

    private static async Task<int> InspectAsync(string databasePath)
    {
        if (!File.Exists(databasePath))
        {
            Console.Error.WriteLine($"Database does not exist: {databasePath}");
            return 2;
        }

        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString());
        await connection.OpenAsync();

        Console.WriteLine($"Database: {databasePath}");
        Console.WriteLine("Memories by scope and type:");
        var command = connection.CreateCommand();
        command.CommandText = "SELECT scope, type, COUNT(*) FROM memory_entries GROUP BY scope, type ORDER BY scope, type;";
        await using var reader = await command.ExecuteReaderAsync();
        var hasRows = false;
        while (await reader.ReadAsync())
        {
            hasRows = true;
            Console.WriteLine($"  {reader.GetString(0),-8} {reader.GetString(1),-10} {reader.GetInt64(2)}");
        }
        if (!hasRows)
            Console.WriteLine("  (none)");

        var owners = connection.CreateCommand();
        owners.CommandText = "SELECT user_id, COUNT(*) FROM memory_entries WHERE scope = 'Private' GROUP BY user_id ORDER BY user_id;";
        Console.WriteLine("Private memory owners:");
        string? firstOwner = null;
        await using var ownerReader = await owners.ExecuteReaderAsync();
        while (await ownerReader.ReadAsync())
        {
            firstOwner ??= ownerReader.GetString(0);
            Console.WriteLine($"  {ownerReader.GetString(0)}: {ownerReader.GetInt64(1)}");
        }

        var profiles = connection.CreateCommand();
        profiles.CommandText = "SELECT COUNT(*) FROM user_profiles;";
        Console.WriteLine($"User profiles: {await profiles.ExecuteScalarAsync()}");

        if (firstOwner is not null)
        {
            await using var provider = new SqliteMemoryProvider(databasePath);
            var probe = await provider.SearchMemoriesAsync(firstOwner, "用户身份 信息", new MemorySearchOptions
            {
                Type = MemoryType.User
            });
            Console.WriteLine($"Probe search (用户身份 信息, User): {probe.Count} result(s)");
        }
        return 0;
    }

    private static async Task MigrateProfilesAsync(
        string privateDirectory,
        SqliteMemoryProvider? destination,
        bool apply,
        MigrationReport report)
    {
        if (!Directory.Exists(privateDirectory))
            return;

        foreach (var userDirectory in Directory.EnumerateDirectories(privateDirectory))
        {
            var profilePath = Path.Combine(userDirectory, "user-profile.json");
            if (!File.Exists(profilePath))
                continue;

            try
            {
                var profile = JsonSerializer.Deserialize<UserProfile>(await File.ReadAllTextAsync(profilePath));
                if (profile is null)
                {
                    report.SkippedProfiles++;
                    report.Errors.Add($"Invalid user profile: {profilePath}");
                    continue;
                }

                // The legacy path defines the profile's owner, even if JSON is incomplete.
                profile.UserId = Path.GetFileName(userDirectory);
                if (apply)
                    await destination!.SaveUserProfileAsync(profile);
                report.MigratedProfiles++;
            }
            catch (Exception exception)
            {
                report.SkippedProfiles++;
                report.Errors.Add($"{profilePath}: {exception.Message}");
            }
        }
    }

    private sealed class MigrationReport
    {
        public int MigratedMemories { get; set; }
        public int SkippedMemories { get; set; }
        public int MigratedProfiles { get; set; }
        public int SkippedProfiles { get; set; }
        public List<string> Errors { get; } = [];
    }

    private sealed record MigrationOptions(string SourceDirectory, string DatabasePath, bool Apply, bool Inspect, bool ShowHelp)
    {
        public static string HelpText => """
            InsightaAI legacy memory migrator

            Usage:
              dotnet run --project src/InsightaAI.Memory.Migrator -- [options]

            Options:
              --source <directory>   Legacy ~/.insighta/memories directory
              --database <path>      Destination SQLite database path
              --apply                Write data. Without this flag the command is a dry-run.
              --inspect              Read-only summary of a migrated SQLite database.
              --help, -h             Show this help.

            Migration is idempotent: --apply upserts by the existing memory ID and never deletes
            source Markdown files.
            """;

        public static MigrationOptions Parse(string[] args)
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".insighta",
                "memories");
            var source = root;
            var database = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".insighta",
                "memories",
                "memories.db");
            var apply = false;
            var inspect = false;
            var showHelp = false;

            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index])
                {
                    case "--source":
                        source = ReadValue(args, ref index, "--source");
                        break;
                    case "--database":
                        database = ReadValue(args, ref index, "--database");
                        break;
                    case "--apply":
                        apply = true;
                        break;
                    case "--help" or "-h":
                        showHelp = true;
                        break;
                    case "--inspect":
                        inspect = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown option: {args[index]}. Use --help for usage.");
                }
            }

            return new MigrationOptions(Path.GetFullPath(source), Path.GetFullPath(database), apply, inspect, showHelp);
        }

        private static string ReadValue(string[] args, ref int index, string option)
        {
            if (++index >= args.Length || args[index].StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException($"Option {option} requires a value.");
            return args[index];
        }
    }
}
