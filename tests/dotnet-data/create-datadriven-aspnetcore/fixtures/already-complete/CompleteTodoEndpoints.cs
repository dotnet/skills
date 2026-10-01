using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace CompleteTodoApi;

public sealed class TodoItem
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public bool IsComplete { get; set; }
}

public sealed record CreateTodoRequest(string Title, bool IsComplete);
public sealed record UpdateTodoRequest(string Title, bool IsComplete);

public sealed class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
{
    public DbSet<TodoItem> TodoItems => Set<TodoItem>();
}

public static class TodoEndpoints
{
    public static IEndpointRouteBuilder MapTodoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/todos").WithTags("Todos");

        group.MapGet("/", (TodoDbContext db) => db.TodoItems.AsNoTracking().ToListAsync())
            .WithName("GetTodos")
            .WithDescription("Returns all todo items")
            .Produces<List<TodoItem>>(StatusCodes.Status200OK);

        group.MapGet("/{id:int}", async (int id, TodoDbContext db) =>
                await db.TodoItems.FindAsync(id) is { } item ? Results.Ok(item) : Results.NotFound())
            .WithName("GetTodo")
            .WithDescription("Returns one todo item")
            .Produces<TodoItem>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound);

        group.MapPost("/", async (CreateTodoRequest input, TodoDbContext db) =>
            {
                if (string.IsNullOrWhiteSpace(input.Title))
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]> { ["title"] = ["Title is required."] });
                }

                var item = new TodoItem { Title = input.Title, IsComplete = input.IsComplete };
                db.TodoItems.Add(item);
                await db.SaveChangesAsync();
                return Results.Created($"/todos/{item.Id}", item);
            })
            .WithName("CreateTodo")
            .WithDescription("Creates a todo item")
            .ProducesValidationProblem()
            .Produces<TodoItem>(StatusCodes.Status201Created);

        group.MapPut("/{id:int}", async (int id, UpdateTodoRequest input, TodoDbContext db) =>
            {
                if (string.IsNullOrWhiteSpace(input.Title))
                {
                    return Results.ValidationProblem(
                        new Dictionary<string, string[]> { ["title"] = ["Title is required."] });
                }

                var item = await db.TodoItems.FindAsync(id);
                if (item is null)
                {
                    return Results.NotFound();
                }

                item.Title = input.Title;
                item.IsComplete = input.IsComplete;
                await db.SaveChangesAsync();
                return Results.NoContent();
            })
            .WithName("UpdateTodo")
            .WithDescription("Updates a todo item")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound)
            .ProducesValidationProblem();

        group.MapDelete("/{id:int}", async (int id, TodoDbContext db) =>
            {
                var deleted = await db.TodoItems.Where(item => item.Id == id).ExecuteDeleteAsync();
                return deleted == 0 ? Results.NotFound() : Results.NoContent();
            })
            .WithName("DeleteTodo")
            .WithDescription("Deletes a todo item")
            .Produces(StatusCodes.Status204NoContent)
            .Produces(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
