using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace SkillValidator.Services;

/// <summary>
/// Tracks eval sessions in a SQLite database for crash recovery and rejudging.
/// Thread-safe for concurrent scenario/run execution.
/// </summary>
public sealed class SessionDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public SessionDatabase(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _connection = new SqliteConnection($"Data Source={dbPath}");
        _connection.Open();
        Initialize();
    }

    private void Initialize()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            PRAGMA journal_mode=WAL;
            PRAGMA busy_timeout=5000;

            CREATE TABLE IF NOT EXISTS schema_info (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            INSERT OR IGNORE INTO schema_info (key, value) VALUES ('type', 'skill-validator');
            INSERT OR IGNORE INTO schema_info (key, value) VALUES ('version', '1');

            CREATE TABLE IF NOT EXISTS sessions (
                id TEXT PRIMARY KEY,
                skill_name TEXT NOT NULL,
                skill_path TEXT NOT NULL,
                scenario_name TEXT NOT NULL,
                run_index INTEGER NOT NULL,
                role TEXT NOT NULL,
                model TEXT NOT NULL,
                config_dir TEXT,
                work_dir TEXT,
                prompt TEXT,
                skill_sha TEXT,
                status TEXT NOT NULL DEFAULT 'running',
                started_at TEXT NOT NULL,
                completed_at TEXT
            );

            CREATE TABLE IF NOT EXISTS run_results (
                session_id TEXT PRIMARY KEY REFERENCES sessions(id),
                metrics_json TEXT NOT NULL,
                judge_json TEXT,
                pairwise_json TEXT
            );
            """;
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Computes a SHA-256 hash over all files in a directory, sorted by relative path.
    /// Returns the first 12 hex characters for a short, collision-resistant identifier.
    /// </summary>
    public static string ComputeDirectorySha(string dirPath)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var files = Directory.GetFiles(dirPath, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(dirPath, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        foreach (var relPath in files)
        {
            sha.AppendData(System.Text.Encoding.UTF8.GetBytes(relPath));
            sha.AppendData(File.ReadAllBytes(Path.Combine(dirPath, relPath)));
        }

        var hash = sha.GetHashAndReset();
        return Convert.ToHexString(hash)[..12].ToLowerInvariant();
    }

    public void RegisterSession(string sessionId, string skillName, string skillPath,
        string scenarioName, int runIndex, string role, string model,
        string? configDir, string? workDir, string? prompt = null, string? skillSha = null)
    {
        _writeLock.Wait();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO sessions (id, skill_name, skill_path, scenario_name, run_index, role, model, config_dir, work_dir, prompt, skill_sha, status, started_at)
                VALUES ($id, $skill_name, $skill_path, $scenario_name, $run_index, $role, $model, $config_dir, $work_dir, $prompt, $skill_sha, 'running', $started_at)
                """;
            cmd.Parameters.AddWithValue("$id", sessionId);
            cmd.Parameters.AddWithValue("$skill_name", skillName);
            cmd.Parameters.AddWithValue("$skill_path", skillPath);
            cmd.Parameters.AddWithValue("$scenario_name", scenarioName);
            cmd.Parameters.AddWithValue("$run_index", runIndex);
            cmd.Parameters.AddWithValue("$role", role);
            cmd.Parameters.AddWithValue("$model", model);
            cmd.Parameters.AddWithValue("$config_dir", (object?)configDir ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$work_dir", (object?)workDir ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$prompt", (object?)prompt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$skill_sha", (object?)skillSha ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$started_at", DateTimeOffset.UtcNow.ToString("o"));
            cmd.ExecuteNonQuery();
        }
        finally { _writeLock.Release(); }
    }

    public void CompleteSession(string sessionId, string status, string metricsJson)
    {
        _writeLock.Wait();
        try
        {
            using var transaction = _connection.BeginTransaction();

            using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = "UPDATE sessions SET status = $status, completed_at = $completed_at WHERE id = $id";
                cmd.Parameters.AddWithValue("$id", sessionId);
                cmd.Parameters.AddWithValue("$status", status);
                cmd.Parameters.AddWithValue("$completed_at", DateTimeOffset.UtcNow.ToString("o"));
                cmd.ExecuteNonQuery();
            }

            using (var cmd = _connection.CreateCommand())
            {
                cmd.Transaction = transaction;
                cmd.CommandText = """
                    INSERT OR REPLACE INTO run_results (session_id, metrics_json)
                    VALUES ($session_id, $metrics_json)
                    """;
                cmd.Parameters.AddWithValue("$session_id", sessionId);
                cmd.Parameters.AddWithValue("$metrics_json", metricsJson);
                cmd.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        finally { _writeLock.Release(); }
    }

    public void SaveJudgeResult(string sessionId, string judgeJson)
    {
        _writeLock.Wait();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE run_results SET judge_json = $judge_json WHERE session_id = $session_id";
            cmd.Parameters.AddWithValue("$session_id", sessionId);
            cmd.Parameters.AddWithValue("$judge_json", judgeJson);
            cmd.ExecuteNonQuery();
        }
        finally { _writeLock.Release(); }
    }

    public void SavePairwiseResult(string baselineSessionId, string pairwiseJson)
    {
        _writeLock.Wait();
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "UPDATE run_results SET pairwise_json = $pairwise_json WHERE session_id = $session_id";
            cmd.Parameters.AddWithValue("$session_id", baselineSessionId);
            cmd.Parameters.AddWithValue("$pairwise_json", pairwiseJson);
            cmd.ExecuteNonQuery();
        }
        finally { _writeLock.Release(); }
    }

    /// <summary>
    /// Returns all completed sessions grouped by (skill, scenario, run_index).
    /// </summary>
    public List<SessionRecord> GetCompletedSessions()
    {
        return GetSessions("WHERE s.status IN ('completed', 'timed_out')");
    }

    /// <summary>
    /// Returns schema metadata (type, version) for DB detection by external tools.
    /// </summary>
    public Dictionary<string, string> GetSchemaInfo()
    {
        var result = new Dictionary<string, string>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM schema_info";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }

    private List<SessionRecord> GetSessions(string whereClause)
    {
        var results = new List<SessionRecord>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"""
            SELECT s.id, s.skill_name, s.skill_path, s.scenario_name, s.run_index, s.role, s.model,
                   s.config_dir, s.work_dir, s.prompt, s.skill_sha, s.status,
                   r.metrics_json, r.judge_json, r.pairwise_json
            FROM sessions s
            LEFT JOIN run_results r ON s.id = r.session_id
            {whereClause}
            ORDER BY s.skill_name, s.scenario_name, s.run_index, s.role
            """;
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            results.Add(new SessionRecord(
                Id: reader.GetString(0),
                SkillName: reader.GetString(1),
                SkillPath: reader.GetString(2),
                ScenarioName: reader.GetString(3),
                RunIndex: reader.GetInt32(4),
                Role: reader.GetString(5),
                Model: reader.GetString(6),
                ConfigDir: reader.IsDBNull(7) ? null : reader.GetString(7),
                WorkDir: reader.IsDBNull(8) ? null : reader.GetString(8),
                Prompt: reader.IsDBNull(9) ? null : reader.GetString(9),
                SkillSha: reader.IsDBNull(10) ? null : reader.GetString(10),
                Status: reader.GetString(11),
                MetricsJson: reader.IsDBNull(12) ? null : reader.GetString(12),
                JudgeJson: reader.IsDBNull(13) ? null : reader.GetString(13),
                PairwiseJson: reader.IsDBNull(14) ? null : reader.GetString(14)));
        }
        return results;
    }

    public void Dispose()
    {
        _connection.Dispose();
        _writeLock.Dispose();
    }
}

public sealed record SessionRecord(
    string Id,
    string SkillName,
    string SkillPath,
    string ScenarioName,
    int RunIndex,
    string Role,
    string Model,
    string? ConfigDir,
    string? WorkDir,
    string? Prompt,
    string? SkillSha,
    string Status,
    string? MetricsJson,
    string? JudgeJson,
    string? PairwiseJson);
