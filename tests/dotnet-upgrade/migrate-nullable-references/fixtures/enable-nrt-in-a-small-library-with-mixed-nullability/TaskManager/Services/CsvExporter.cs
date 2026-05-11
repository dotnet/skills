using TaskManager.Models;

namespace TaskManager.Services;

public class CsvExporter
{
    private string _separator;
    private string _header;

    public CsvExporter(string separator = ",")
    {
        _separator = separator;
    }

    public string Export(IEnumerable<TaskItem> tasks)
    {
        _header = string.Join(_separator, "Id", "Title", "AssignedTo", "Completed");
        var lines = new List<string> { _header };
        foreach (var task in tasks)
        {
            var assignee = task.AssignedTo ?? "";
            lines.Add(string.Join(_separator,
                task.Id, task.Title, assignee, task.IsCompleted));
        }
        return string.Join(Environment.NewLine, lines);
    }
}
