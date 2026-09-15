using System.Security.Cryptography;
using System.Text;

namespace BlazorComponentReadiness.Validator.IO;

public static class DomainSeparatedHash
{
    private static readonly byte[] Prefix = Encoding.UTF8.GetBytes("blazor-component-readiness\0");

    public static string Compute(string domain, ReadOnlySpan<byte> content)
    {
        ValidateDomain(domain);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Prefix);
        hash.AppendData(Encoding.UTF8.GetBytes(domain));
        hash.AppendData([0]);
        hash.AppendData(content);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    public static string ComputeFile(string domain, string path, long maximumBytes, string resource)
    {
        ValidateDomain(domain);
        BoundedIO.EnsureFileLength(path, maximumBytes, resource);

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Prefix);
        hash.AppendData(Encoding.UTF8.GetBytes(domain));
        hash.AppendData([0]);

        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            total = checked(total + read);
            BoundedIO.EnsureLength(total, maximumBytes, resource);
            hash.AppendData(buffer, 0, read);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void ValidateDomain(string domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        if (domain.Length > 128 ||
            domain.Any(character =>
                character is < 'a' or > 'z' && character is < '0' or > '9' && character is not '-' and not '.'))
        {
            throw new ArgumentException(
                "Hash domains must contain only lowercase ASCII letters, digits, hyphens, or periods.",
                nameof(domain));
        }
    }
}
