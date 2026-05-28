namespace TaskManager.Models;

public class TaskItem
{
    public int Id { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public string AssignedTo { get; set; }
    public DateTime? DueDate { get; set; }
    public bool IsCompleted { get; set; }
    public List<string> Tags { get; set; } = new();

    public TaskItem(string title)
    {
        Title = title;
    }
}
