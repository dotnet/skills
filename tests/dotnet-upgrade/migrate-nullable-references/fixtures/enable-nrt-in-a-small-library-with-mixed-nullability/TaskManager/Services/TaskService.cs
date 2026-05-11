using TaskManager.Models;

namespace TaskManager.Services;

public class TaskService
{
    private readonly ITaskRepository _repository;
    private string _defaultAssignee;

    public TaskService(ITaskRepository repository, string defaultAssignee)
    {
        _repository = repository;
        _defaultAssignee = defaultAssignee;
    }

    public TaskItem CreateTask(string title, string description, string assignedTo = null)
    {
        var task = new TaskItem(title)
        {
            Description = description,
            AssignedTo = assignedTo ?? _defaultAssignee
        };
        _repository.Add(task);
        return task;
    }

    public TaskItem GetTask(int id) => _repository.GetById(id);

    public bool CompleteTask(int id)
    {
        var task = _repository.GetById(id);
        if (task == null) return false;
        task.IsCompleted = true;
        return true;
    }

    public IReadOnlyList<TaskItem> GetTasksByAssignee(string assignee)
    {
        return _repository.GetAll()
            .Where(t => string.Equals(t.AssignedTo, assignee, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
