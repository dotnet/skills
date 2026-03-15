using System.Text.Json;
using Microsoft.Data.Sqlite;
using SkillValidator.Services;

namespace SkillValidator.Tests;

public class SessionDatabaseTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SessionDatabase _db;

    public SessionDatabaseTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"test-sessions-{Guid.NewGuid()}.db");
        _db = new SessionDatabase(_dbPath);
    }

    public void Dispose()
    {
        _db.Dispose();
        // Clear SQLite connection pool so file handles are fully released
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        TryDelete(_dbPath);
        TryDelete(_dbPath + "-wal");
        TryDelete(_dbPath + "-shm");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort cleanup */ }
    }

    [Fact]
    public void RegisterAndComplete_RoundTrips()
    {
        var rubricJson = JsonSerializer.Serialize(new[] { "Quality", "Completeness" });

        _db.RegisterSession("s1", "my-skill", "/path/to/skill", "scenario-a", 0, "baseline", "gpt-4.1", "sessions/s1", "/work", "Fix the bug", "abcdef012345", rubricJson);
        _db.CompleteSession("s1", "completed", """{"TokenEstimate":100}""");

        var sessions = _db.GetCompletedSessions();
        var s = Assert.Single(sessions);
        Assert.Equal("s1", s.Id);
        Assert.Equal("my-skill", s.SkillName);
        Assert.Equal("/path/to/skill", s.SkillPath);
        Assert.Equal("scenario-a", s.ScenarioName);
        Assert.Equal(0, s.RunIndex);
        Assert.Equal("baseline", s.Role);
        Assert.Equal("gpt-4.1", s.Model);
        Assert.Equal("sessions/s1", s.ConfigDir);
        Assert.Equal("completed", s.Status);
        Assert.Equal("Fix the bug", s.Prompt);
        Assert.Equal("abcdef012345", s.SkillSha);
        Assert.Equal(rubricJson, s.RubricJson);
        Assert.Equal("""{"TokenEstimate":100}""", s.MetricsJson);
        Assert.Null(s.JudgeJson);
        Assert.Null(s.PairwiseJson);
    }

    [Fact]
    public void SaveJudgeResult_UpdatesExistingRow()
    {
        _db.RegisterSession("s1", "skill", "/p", "scn", 0, "baseline", "model", null, null);
        _db.CompleteSession("s1", "completed", "{}");
        _db.SaveJudgeResult("s1", """{"OverallScore":4}""");

        var s = Assert.Single(_db.GetCompletedSessions());
        Assert.Equal("""{"OverallScore":4}""", s.JudgeJson);
    }

    [Fact]
    public void SavePairwiseResult_UpdatesExistingRow()
    {
        _db.RegisterSession("s1", "skill", "/p", "scn", 0, "baseline", "model", null, null);
        _db.CompleteSession("s1", "completed", "{}");
        _db.SavePairwiseResult("s1", """{"Winner":"with-skill"}""");

        var s = Assert.Single(_db.GetCompletedSessions());
        Assert.Equal("""{"Winner":"with-skill"}""", s.PairwiseJson);
    }

    [Fact]
    public void RegisterWithoutPromptOrSkillSha_StoresNulls()
    {
        _db.RegisterSession("s1", "skill", "/p", "scn", 0, "baseline", "model", null, null);
        _db.CompleteSession("s1", "completed", "{}");

        var s = Assert.Single(_db.GetCompletedSessions());
        Assert.Null(s.Prompt);
        Assert.Null(s.SkillSha);
        Assert.Null(s.RubricJson);
    }

    [Fact]
    public void GetCompletedSessions_ExcludesRunning()
    {
        _db.RegisterSession("s1", "skill", "/p", "scn", 0, "baseline", "model", null, null);
        // Never completed — should not appear
        var sessions = _db.GetCompletedSessions();
        Assert.Empty(sessions);
    }

    [Fact]
    public void GetCompletedSessions_IncludesTimedOut()
    {
        _db.RegisterSession("s1", "skill", "/p", "scn", 0, "baseline", "model", null, null);
        _db.CompleteSession("s1", "timed_out", "{}");

        var sessions = _db.GetCompletedSessions();
        Assert.Single(sessions);
        Assert.Equal("timed_out", sessions[0].Status);
    }

    [Fact]
    public void MultipleSessions_OrderedCorrectly()
    {
        // Register pairs for two scenarios
        _db.RegisterSession("b0", "skill", "/p", "alpha", 0, "baseline", "m", null, null);
        _db.RegisterSession("w0", "skill", "/p", "alpha", 0, "with-skill", "m", null, null);
        _db.RegisterSession("b1", "skill", "/p", "beta", 0, "baseline", "m", null, null);
        _db.RegisterSession("w1", "skill", "/p", "beta", 0, "with-skill", "m", null, null);

        _db.CompleteSession("b0", "completed", "{}");
        _db.CompleteSession("w0", "completed", "{}");
        _db.CompleteSession("b1", "completed", "{}");
        _db.CompleteSession("w1", "completed", "{}");

        var sessions = _db.GetCompletedSessions();
        Assert.Equal(4, sessions.Count);
        // Ordered by skill_name, scenario_name, run_index, role
        Assert.Equal("alpha", sessions[0].ScenarioName);
        Assert.Equal("baseline", sessions[0].Role);
        Assert.Equal("alpha", sessions[1].ScenarioName);
        Assert.Equal("with-skill", sessions[1].Role);
        Assert.Equal("beta", sessions[2].ScenarioName);
    }

    [Fact]
    public async Task ConcurrentWrites_DoNotCorrupt()
    {
        const int count = 20;
        var tasks = Enumerable.Range(0, count).Select(i => Task.Run(() =>
        {
            var id = $"s{i}";
            _db.RegisterSession(id, "skill", "/p", "scn", i, i % 2 == 0 ? "baseline" : "with-skill", "m", null, null);
            _db.CompleteSession(id, "completed", $"{{\"Index\":{i}}}");
            _db.SaveJudgeResult(id, $"{{\"Score\":{i}}}");
        }));

        await Task.WhenAll(tasks);

        var sessions = _db.GetCompletedSessions();
        Assert.Equal(count, sessions.Count);
        Assert.All(sessions, s =>
        {
            Assert.Equal("completed", s.Status);
            Assert.NotNull(s.MetricsJson);
            Assert.NotNull(s.JudgeJson);
        });
    }

    [Fact]
    public void ComputeDirectorySha_IsDeterministic()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sha-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "SKILL.md"), "# Test Skill");
            File.WriteAllText(Path.Combine(dir, "plugin.json"), "{}");

            var sha1 = SessionDatabase.ComputeDirectorySha(dir);
            var sha2 = SessionDatabase.ComputeDirectorySha(dir);
            Assert.Equal(sha1, sha2);
            Assert.Equal(12, sha1.Length);

            // Changing content produces a different SHA
            File.WriteAllText(Path.Combine(dir, "SKILL.md"), "# Modified");
            var sha3 = SessionDatabase.ComputeDirectorySha(dir);
            Assert.NotEqual(sha1, sha3);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ComputeDirectorySha_DistinguishesPathAndContentBoundaries()
    {
        var dir1 = Path.Combine(Path.GetTempPath(), $"sha-boundary-a-{Guid.NewGuid()}");
        var dir2 = Path.Combine(Path.GetTempPath(), $"sha-boundary-b-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir1);
        Directory.CreateDirectory(dir2);
        try
        {
            File.WriteAllText(Path.Combine(dir1, "a"), "12");
            File.WriteAllText(Path.Combine(dir1, "b"), "34");

            File.WriteAllText(Path.Combine(dir2, "a1"), "2");
            File.WriteAllText(Path.Combine(dir2, "b"), "34");

            Assert.NotEqual(
                SessionDatabase.ComputeDirectorySha(dir1),
                SessionDatabase.ComputeDirectorySha(dir2));
        }
        finally
        {
            Directory.Delete(dir1, true);
            Directory.Delete(dir2, true);
        }
    }

    [Fact]
    public void SeparateDbFiles_AreIndependent()
    {
        // Simulates two concurrent eval processes using different result dirs
        var dbPath2 = Path.Combine(Path.GetTempPath(), $"test-sessions-{Guid.NewGuid()}.db");
        try
        {
            using var db2 = new SessionDatabase(dbPath2);

            _db.RegisterSession("s1", "skill-a", "/a", "scn", 0, "baseline", "m", null, null);
            _db.CompleteSession("s1", "completed", "{}");

            db2.RegisterSession("s1", "skill-b", "/b", "scn", 0, "baseline", "m", null, null);
            db2.CompleteSession("s1", "completed", "{}");

            // Each DB has exactly one session with different skill names
            var sessions1 = _db.GetCompletedSessions();
            var sessions2 = db2.GetCompletedSessions();
            Assert.Single(sessions1);
            Assert.Single(sessions2);
            Assert.Equal("skill-a", sessions1[0].SkillName);
            Assert.Equal("skill-b", sessions2[0].SkillName);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            TryDelete(dbPath2);
            TryDelete(dbPath2 + "-wal");
            TryDelete(dbPath2 + "-shm");
        }
    }

    [Fact]
    public void SchemaInfo_ContainsTypeAndVersion()
    {
        var info = _db.GetSchemaInfo();
        Assert.Equal("skill-validator", info["type"]);
        Assert.Equal("2", info["version"]);
    }

    [Fact]
    public void SchemaInfo_CanPersistJudgeModel()
    {
        _db.SetSchemaInfo("judge_model", "claude-opus-4.6");

        var info = _db.GetSchemaInfo();
        Assert.Equal("claude-opus-4.6", info["judge_model"]);
    }

    [Fact]
    public void LegacyDatabase_UpgradesRubricColumn()
    {
        var legacyDbPath = Path.Combine(Path.GetTempPath(), $"legacy-sessions-{Guid.NewGuid()}.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={legacyDbPath}"))
            {
                connection.Open();
                using var cmd = connection.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE schema_info (
                        key TEXT PRIMARY KEY,
                        value TEXT NOT NULL
                    );
                    INSERT INTO schema_info (key, value) VALUES ('type', 'skill-validator');
                    INSERT INTO schema_info (key, value) VALUES ('version', '1');

                    CREATE TABLE sessions (
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

                    CREATE TABLE run_results (
                        session_id TEXT PRIMARY KEY REFERENCES sessions(id),
                        metrics_json TEXT NOT NULL,
                        judge_json TEXT,
                        pairwise_json TEXT
                    );

                    INSERT INTO sessions (id, skill_name, skill_path, scenario_name, run_index, role, model, status, started_at, completed_at)
                    VALUES ('s1', 'skill', '/p', 'scn', 0, 'baseline', 'model', 'completed', '2026-01-01T00:00:00Z', '2026-01-01T00:01:00Z');
                    INSERT INTO run_results (session_id, metrics_json) VALUES ('s1', '{}');
                    """;
                cmd.ExecuteNonQuery();
            }

            using var upgradedDb = new SessionDatabase(legacyDbPath);
            var legacySession = Assert.Single(upgradedDb.GetCompletedSessions());
            Assert.Null(legacySession.RubricJson);
            Assert.Equal("2", upgradedDb.GetSchemaInfo()["version"]);

            var rubricJson = JsonSerializer.Serialize(new[] { "Quality" });
            upgradedDb.RegisterSession("s2", "skill", "/p", "scn", 1, "with-skill", "model", null, null, "Prompt", null, rubricJson);
            upgradedDb.CompleteSession("s2", "completed", "{}");

            var upgradedSession = Assert.Single(upgradedDb.GetCompletedSessions(), s => s.Id == "s2");
            Assert.Equal(rubricJson, upgradedSession.RubricJson);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            TryDelete(legacyDbPath);
            TryDelete(legacyDbPath + "-wal");
            TryDelete(legacyDbPath + "-shm");
        }
    }

    [Fact]
    public void CompleteSession_RequiresExistingSession()
    {
        Assert.Throws<Microsoft.Data.Sqlite.SqliteException>(() =>
            _db.CompleteSession("missing", "completed", "{}"));
    }

    [Fact]
    public void ConfigDir_StoredAsRelativePath()
    {
        _db.RegisterSession("s1", "skill", "/p", "scn", 0, "baseline", "m", "sessions/s1", null);
        _db.CompleteSession("s1", "completed", "{}");

        var s = Assert.Single(_db.GetCompletedSessions());
        Assert.Equal("sessions/s1", s.ConfigDir);
    }
}
