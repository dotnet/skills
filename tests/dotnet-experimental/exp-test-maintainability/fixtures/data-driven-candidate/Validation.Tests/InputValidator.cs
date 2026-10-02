namespace Validation.Tests;

public sealed record ValidationResult(bool IsValid, string? ErrorMessage);

public sealed class InputValidator
{
    public ValidationResult Validate(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new(false, "Input cannot be empty");
        }

        if (input.Length < 3)
        {
            return new(false, "Input must be at least 3 characters");
        }

        if (input.Length > 255)
        {
            return new(false, "Input must not exceed 255 characters");
        }

        if (input.Contains('<') || input.Contains('>'))
        {
            return new(false, "Input contains invalid characters");
        }

        return new(true, null);
    }
}
