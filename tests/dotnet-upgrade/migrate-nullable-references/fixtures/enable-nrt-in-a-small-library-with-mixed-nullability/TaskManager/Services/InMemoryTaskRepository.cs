using TaskManager.Models;

namespace TaskManager.Services;

public class InMemoryTaskRepository : ITaskRepository
{
    private readonly List<TaskItem> _tasks = new();
    private int _nextId = 1;

    public TaskItem GetById(int id)
    {
        return _tasks.FirstOrDefault(t => t.Id == id);
    }

    public IReadOnlyList<TaskItem> GetAll() => _tasks.AsReadOnly();

    public void Add(TaskItem task)
    {
        task.Id = _nextId++;
        _tasks.Add(task);
    }

    public void Remove(int id)
    {
        var task = GetById(id);
        if (task != null)
            _tasks.Remove(task);
    }

    public TaskItem FindByTitle(string title)
    {
        return _tasks.FirstOrDefault(t =>
            string.Equals(t.Title, title, StringComparison.OrdinalIgnoreCase));
    }
}
