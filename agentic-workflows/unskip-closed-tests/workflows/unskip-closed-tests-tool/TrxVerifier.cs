using System.Xml;
using System.Xml.Linq;

namespace UnskipClosedTests.Tool;

internal static class TrxVerifier
{
    public static (bool Success, string Reason) Verify(IReadOnlyList<VerificationTest> tests)
    {
        HashSet<string> expectedFiles = tests
            .Select(static test => Path.GetFullPath(test.ResultFile))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (string directory in tests.Select(static test => Path.GetDirectoryName(test.ResultFile)!)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(directory))
            {
                string? unexpected = Directory.EnumerateFiles(directory, "*.trx", SearchOption.TopDirectoryOnly)
                    .Select(Path.GetFullPath)
                    .FirstOrDefault(path => !expectedFiles.Contains(path));
                if (unexpected is not null)
                {
                    return (false, $"unexpected_trx:{Path.GetFileName(unexpected)}");
                }
            }
        }

        foreach (VerificationTest test in tests)
        {
            if (!File.Exists(test.ResultFile))
            {
                return (false, $"missing_trx:{Path.GetFileName(test.ResultFile)}");
            }

            try
            {
                FileInfo resultFile = new(test.ResultFile);
                if (resultFile.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    return (false, $"unsafe_trx:{Path.GetFileName(test.ResultFile)}");
                }

                if (resultFile.Length > 16777216)
                {
                    return (false, $"oversized_trx:{Path.GetFileName(test.ResultFile)}");
                }

                XDocument document = XDocument.Load(test.ResultFile, LoadOptions.None);
                Dictionary<string, string> mappings = new(StringComparer.Ordinal);
                foreach (XElement unitTest in document.Descendants().Where(static element =>
                             element.Name.LocalName == "UnitTest"))
                {
                    string? id = unitTest.Attribute("id")?.Value;
                    XElement? method = unitTest.Descendants().FirstOrDefault(static element =>
                        element.Name.LocalName == "TestMethod");
                    string? className = method?.Attribute("className")?.Value;
                    string? methodName = method?.Attribute("name")?.Value;
                    if (string.IsNullOrWhiteSpace(id) ||
                        string.IsNullOrWhiteSpace(className) ||
                        string.IsNullOrWhiteSpace(methodName))
                    {
                        return (false, "malformed_trx_test_definition");
                    }

                    string fqn = $"{className}.{methodName}";
                    if (!string.Equals(fqn, test.Fqn, StringComparison.Ordinal))
                    {
                        return (false, $"mismatched_fqn:{fqn}");
                    }

                    if (!mappings.TryAdd(id, fqn))
                    {
                        return (false, $"duplicate_trx_test_id:{id}");
                    }
                }

                if (mappings.Count == 0)
                {
                    return (false, "zero_selected_tests");
                }

                List<XElement> results = document.Descendants().Where(static element =>
                    element.Name.LocalName == "UnitTestResult").ToList();
                if (results.Count == 0)
                {
                    return (false, "zero_executed_tests");
                }

                HashSet<string> resultIds = new(StringComparer.Ordinal);
                foreach (XElement result in results)
                {
                    string? testId = result.Attribute("testId")?.Value;
                    string? outcome = result.Attribute("outcome")?.Value;
                    if (string.IsNullOrWhiteSpace(testId) ||
                        !mappings.TryGetValue(testId, out string? mappedFqn))
                    {
                        return (false, "result_without_exact_test_mapping");
                    }

                    if (!resultIds.Add(testId))
                    {
                        return (false, $"duplicate_trx_result_id:{testId}");
                    }

                    if (!string.Equals(mappedFqn, test.Fqn, StringComparison.Ordinal))
                    {
                        return (false, $"mismatched_result_fqn:{mappedFqn}");
                    }

                    if (!string.Equals(outcome, "Passed", StringComparison.OrdinalIgnoreCase))
                    {
                        return (false, $"non_passing_outcome:{outcome ?? "missing"}");
                    }
                }

                string? missingResultId = mappings.Keys.FirstOrDefault(id => !resultIds.Contains(id));
                if (missingResultId is not null)
                {
                    return (false, $"definition_without_result:{missingResultId}");
                }
            }
            catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
            {
                return (false, $"malformed_trx:{ex.GetType().Name}");
            }
        }

        return (true, "passed");
    }
}
