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
        _db.RegisterSession("s1", "my-skill", "/path/to/skill", "scenario-a", 0, "baseline", "gpt-4.1", "/cfg", "/work");
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
        Assert.Equal("completed", s.Status);
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
}
