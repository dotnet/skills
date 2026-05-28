namespace TaskManager;

public sealed class TaskItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string AssignedTo { get; set; } = string.Empty;
    public TaskPriority Priority { get; set; }
    public TaskItemStatus Status { get; set; } = TaskItemStatus.Open;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public enum TaskPriority
{
    Low,
    Medium,
    High,
    Critical
}

public enum TaskItemStatus
{
    Open,
    InProgress,
    Completed,
    Deleted
}
