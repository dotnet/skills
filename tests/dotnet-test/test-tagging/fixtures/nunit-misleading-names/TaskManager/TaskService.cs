using System.Collections.Concurrent;

namespace TaskManager;

public sealed class TaskService
{
    private readonly ConcurrentDictionary<int, TaskItem> _tasks = new();
    private readonly ITaskNotifier _notifier;
    private int _nextId;

    public TaskService(ITaskNotifier notifier)
    {
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
    }

    public TaskItem CreateTask(string title, string assignedTo, TaskPriority priority)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required.", nameof(title));

        if (string.IsNullOrWhiteSpace(assignedTo))
            throw new ArgumentException("AssignedTo is required.", nameof(assignedTo));

        var id = Interlocked.Increment(ref _nextId);
        var task = new TaskItem
        {
            Id = id,
            Title = title,
            AssignedTo = assignedTo,
            Priority = priority,
            Status = TaskItemStatus.Open
        };

        _tasks[id] = task;
        _notifier.NotifyCreated(task);
        return task;
    }

    public TaskItem? GetById(int id) => _tasks.GetValueOrDefault(id);

    public IReadOnlyList<TaskItem> GetByAssignee(string assignedTo)
    {
        return _tasks.Values
            .Where(t => t.AssignedTo.Equals(assignedTo, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public void DeleteTask(int id, string requestedBy)
    {
        if (!_tasks.TryGetValue(id, out var task))
            throw new InvalidOperationException($"Task {id} not found.");

        // Only the assignee or an admin can delete
        if (!task.AssignedTo.Equals(requestedBy, StringComparison.OrdinalIgnoreCase)
            && !requestedBy.Equals("admin", StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Only the assignee or admin can delete a task.");
        }

        task.Status = TaskItemStatus.Deleted;
    }

    public async Task BulkAssignAsync(IEnumerable<int> taskIds, string assignee, CancellationToken ct = default)
    {
        var tasks = new List<Task>();
        foreach (var id in taskIds)
        {
            if (_tasks.TryGetValue(id, out var task))
            {
                tasks.Add(Task.Run(() =>
                {
                    task.AssignedTo = assignee;
                    _notifier.NotifyCreated(task);
                }, ct));
            }
        }

        await Task.WhenAll(tasks);
    }

    public IReadOnlyList<TaskItem> GetTasksByPriorityRange(TaskPriority minPriority, TaskPriority maxPriority)
    {
        if (minPriority > maxPriority)
            throw new ArgumentException("minPriority cannot exceed maxPriority.");

        return _tasks.Values
            .Where(t => t.Priority >= minPriority && t.Priority <= maxPriority)
            .ToList();
    }
}

public interface ITaskNotifier
{
    void NotifyCreated(TaskItem task);
}
