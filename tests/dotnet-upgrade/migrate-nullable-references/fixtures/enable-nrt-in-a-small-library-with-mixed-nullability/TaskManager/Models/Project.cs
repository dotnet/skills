namespace TaskManager.Models;

public class Project
{
    public string Name { get; set; }
    public string Owner { get; set; }
    public List<TaskItem> Tasks { get; } = new();

    public Project(string name, string owner)
    {
        Name = name;
        Owner = owner;
    }

    public TaskItem FindTaskByTitle(string title)
    {
        return Tasks.FirstOrDefault(t => t.Title == title);
    }
}
