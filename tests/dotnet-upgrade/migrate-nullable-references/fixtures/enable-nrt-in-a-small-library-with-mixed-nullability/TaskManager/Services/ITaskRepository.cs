using TaskManager.Models;

namespace TaskManager.Services;

public interface ITaskRepository
{
    TaskItem GetById(int id);
    IReadOnlyList<TaskItem> GetAll();
    void Add(TaskItem task);
    void Remove(int id);
    TaskItem FindByTitle(string title);
}
