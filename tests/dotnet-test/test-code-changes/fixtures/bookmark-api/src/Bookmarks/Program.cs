using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var bookmarks = new ConcurrentDictionary<int, Bookmark>();
bookmarks[1] = new(1, "Field guide", false);

app.MapGet("/bookmarks/{id:int}", (int id) =>
    bookmarks.TryGetValue(id, out var bookmark) ? Results.Ok(bookmark) : Results.NotFound());

app.Run();

public sealed record Bookmark(int Id, string Title, bool Archived);
public partial class Program { }
