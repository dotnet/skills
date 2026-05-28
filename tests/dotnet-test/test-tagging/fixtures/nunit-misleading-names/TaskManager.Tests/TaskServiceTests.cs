using NUnit.Framework;
using TaskManager;

namespace TaskManager.Tests;

[TestFixture]
public sealed class TaskServiceTests
{
    private TaskService _service = null!;
    private FakeTaskNotifier _notifier = null!;

    [SetUp]
    public void Setup()
    {
        _notifier = new FakeTaskNotifier();
        _service = new TaskService(_notifier);
    }

    // Name says "Success" but the body tests an authorization check —
    // a naive name-based classifier would tag this as "positive" only,
    // but reading the body reveals it's a security test (negative path).
    [Test]
    public void DeleteTask_Success()
    {
        var task = _service.CreateTask("Fix bug", "alice", TaskPriority.High);

        Assert.Throws<UnauthorizedAccessException>(
            () => _service.DeleteTask(task.Id, "bob"));
    }

    // Name says "Fails" suggesting negative, but the body verifies
    // correct successful creation — it's actually a positive test.
    [Test]
    public void CreateTask_Fails_WhenFieldsPresent()
    {
        var task = _service.CreateTask("Write docs", "charlie", TaskPriority.Low);

        Assert.That(task.Id, Is.GreaterThan(0));
        Assert.That(task.Title, Is.EqualTo("Write docs"));
        Assert.That(task.Status, Is.EqualTo(TaskItemStatus.Open));
    }

    // Name says "Error" suggesting negative, but the body actually
    // tests a boundary condition with empty result — it's a positive
    // boundary test (valid query that returns empty).
    [Test]
    public void GetByAssignee_Error_NoTasks()
    {
        _service.CreateTask("Task A", "alice", TaskPriority.High);

        var result = _service.GetByAssignee("nobody");

        Assert.That(result, Is.Empty);
    }

    // Name is generic but the body uses Task.WhenAll for concurrent
    // assignment — this is a concurrency test.
    [Test]
    public async Task BulkAssign_UpdatesAllTasks()
    {
        var t1 = _service.CreateTask("A", "alice", TaskPriority.Low);
        var t2 = _service.CreateTask("B", "alice", TaskPriority.Medium);
        var t3 = _service.CreateTask("C", "alice", TaskPriority.High);

        await _service.BulkAssignAsync(new[] { t1.Id, t2.Id, t3.Id }, "bob");

        Assert.That(_service.GetById(t1.Id)!.AssignedTo, Is.EqualTo("bob"));
        Assert.That(_service.GetById(t2.Id)!.AssignedTo, Is.EqualTo("bob"));
        Assert.That(_service.GetById(t3.Id)!.AssignedTo, Is.EqualTo("bob"));
    }

    // Name is misleading — says "Returns" suggesting a simple positive test,
    // but the body tests the boundary of priority range enum values.
    [Test]
    public void GetTasksByPriorityRange_Returns()
    {
        _service.CreateTask("Low task", "alice", TaskPriority.Low);
        _service.CreateTask("Critical task", "alice", TaskPriority.Critical);

        var result = _service.GetTasksByPriorityRange(TaskPriority.Low, TaskPriority.Low);

        Assert.That(result, Has.Count.EqualTo(1));
        Assert.That(result[0].Priority, Is.EqualTo(TaskPriority.Low));
    }

    // Name says "Validation" suggesting a basic check, but the body
    // tests that admin can delete another user's task — a security /
    // authorization positive test.
    [Test]
    public void DeleteTask_Validation()
    {
        var task = _service.CreateTask("Fix bug", "alice", TaskPriority.High);

        _service.DeleteTask(task.Id, "admin");

        Assert.That(task.Status, Is.EqualTo(TaskItemStatus.Deleted));
    }

    // Name is vague but body tests inverted priority range —
    // this is a negative boundary test.
    [Test]
    public void GetTasksByPriorityRange_InvalidRange()
    {
        Assert.Throws<ArgumentException>(
            () => _service.GetTasksByPriorityRange(TaskPriority.Critical, TaskPriority.Low));
    }

    // Straightforward negative test — name matches body.
    [Test]
    public void CreateTask_NullTitle_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(
            () => _service.CreateTask(null!, "alice", TaskPriority.High));
    }

    // Name says "Check" — vague. Body verifies the assignee owns
    // the task and can delete it (authorization positive path).
    [Test]
    public void DeleteTask_Check_AssigneeCanDelete()
    {
        var task = _service.CreateTask("Clean up", "alice", TaskPriority.Medium);

        _service.DeleteTask(task.Id, "alice");

        Assert.That(task.Status, Is.EqualTo(TaskItemStatus.Deleted));
    }

    // Name says nothing about concurrency, but the body creates
    // tasks in rapid succession checking thread-safe ID generation.
    [Test]
    public void CreateTask_MultipleRapidCreations_UniqueIds()
    {
        var tasks = Enumerable.Range(0, 50)
            .Select(i => _service.CreateTask($"Task {i}", "alice", TaskPriority.Low))
            .ToList();

        var uniqueIds = tasks.Select(t => t.Id).Distinct().Count();
        Assert.That(uniqueIds, Is.EqualTo(50));
    }
}

internal sealed class FakeTaskNotifier : ITaskNotifier
{
    public List<TaskItem> Notifications { get; } = [];

    public void NotifyCreated(TaskItem task)
    {
        Notifications.Add(task);
    }
}
