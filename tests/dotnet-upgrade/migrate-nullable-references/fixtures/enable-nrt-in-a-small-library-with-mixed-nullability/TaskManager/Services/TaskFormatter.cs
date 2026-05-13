using TaskManager.Models;

namespace TaskManager.Services;

public static class TaskFormatter
{
    public static string FormatTask(TaskItem task)
    {
        var status = task.IsCompleted ? "Done" : "Pending";
        var assignee = task.AssignedTo ?? "Unassigned";
        var due = task.DueDate.HasValue
            ? task.DueDate.Value.ToShortDateString()
            : "No due date";
        return $"[{status}] {task.Title} - {assignee} (Due: {due})";
    }

    public static string FormatProject(Project project)
    {
        var lines = new List<string>
        {
            $"Project: {project.Name} (Owner: {project.Owner})",
            $"Tasks: {project.Tasks.Count}"
        };
        foreach (var task in project.Tasks)
        {
            lines.Add($"  - {FormatTask(task)}");
        }
        return string.Join(Environment.NewLine, lines);
    }
}
