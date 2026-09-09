namespace Catalog;

public readonly record struct ProductCode(string Value)
{
    public ProductCode Normalize() => new(Value.Trim().ToUpperInvariant());
}
