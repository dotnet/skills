using System.ComponentModel.DataAnnotations;

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.MapPost("/api/contacts", (CreateContactRequest request) =>
    TypedResults.Created("/api/contacts/1", new ContactResponse(1, request.Name, request.Email)));

app.Run();

public sealed record CreateContactRequest
{
    [Required, MaxLength(100)]
    public required string Name { get; init; }

    [Required, EmailAddress]
    public required string Email { get; init; }
}

public sealed record ContactResponse(int Id, string Name, string Email);

public partial class Program;
