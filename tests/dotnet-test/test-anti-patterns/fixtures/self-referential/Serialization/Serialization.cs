using System.Text.Json;

namespace Serialization;

public class UserDto
{
    public string Name { get; set; } = "";
    public int Age { get; set; }

    public string GetDisplayName() => Name;
}

public class UserEntity
{
    public int Id { get; set; }
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
}

public record Money(decimal Amount, string Currency)
{
    public override string ToString() => $"{Amount:0.00} {Currency}";

    public static Money Parse(string text)
    {
        var parts = text.Split(' ');
        return new Money(decimal.Parse(parts[0]), parts[1]);
    }
}

public class Config
{
    public int Timeout { get; set; }
    public int Retries { get; set; }

    public Config Clone() => new() { Timeout = Timeout, Retries = Retries };
}

public static class Serializer
{
    public static string ToJson<T>(T value) => JsonSerializer.Serialize(value);
    public static T FromJson<T>(string json) => JsonSerializer.Deserialize<T>(json)!;
}

public static class NameNormalizer
{
    public static string Normalize(string name) => name.Trim().ToLowerInvariant();
}

public static class Mapper
{
    public static UserDto ToDto(UserEntity entity) =>
        new() { Name = entity.FullName, Age = 0 };

    public static UserEntity ToEntity(UserDto dto) =>
        new() { Id = 0, FullName = dto.Name, Email = "" };
}

public static class Validator
{
    public static string ValidateEmail(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || !input.Contains('@'))
            throw new ArgumentException("Invalid email", nameof(input));
        return input;
    }
}
