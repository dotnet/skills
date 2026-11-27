using Catalog;

var failures = new List<string>();
var total = 0;

void Expect(string behavior, Func<string> actual, string expected)
{
    total++;
    try
    {
        var value = actual();
        if (value != expected)
        {
            failures.Add($"{behavior}: expected '{expected}', got '{value}'");
        }
    }
    catch (Exception ex)
    {
        failures.Add($"{behavior} ({ex.GetType().Name})");
    }
}

Expect("the reported category collapses to one dash", () => SlugHelper.ToSlug("Tools & Hardware"), "tools-hardware");
Expect("product URLs use the collapsed category", () => ProductUrlBuilder.For("Tools & Hardware", "Claw Hammer", 42), "/products/tools-hardware/claw-hammer-42");
Expect("category URLs collapse every segment", () => CategoryUrlBuilder.For("Bath & Body", "Soap -- Bars"), "/c/bath-body/soap-bars");
Expect("a dash between spaces collapses", () => SlugHelper.ToSlug("a - b"), "a-b");
Expect("outer runs are still trimmed", () => SlugHelper.ToSlug("--Sale!!--"), "sale");
Expect("padded names are still trimmed", () => SlugHelper.ToSlug(" Patio! "), "patio");
Expect("single separators are unchanged", () => SlugHelper.ToSlug("Garden Tools"), "garden-tools");
Expect("digits are kept", () => SlugHelper.ToSlug("Age 3 Plus"), "age-3-plus");
Expect("symbol-only text becomes empty", () => SlugHelper.ToSlug("!!!"), "");
Expect("empty text stays empty", () => SlugHelper.ToSlug(""), "");

if (failures.Count > 0)
{
    Console.Error.WriteLine($"{failures.Count} of {total} fix checks failed:");
    failures.ForEach(f => Console.Error.WriteLine($"  - {f}"));
    return 1;
}

Console.WriteLine($"fix checks passed: {total}");
return 0;
