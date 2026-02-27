using System.Globalization;

namespace Textualizer;

/// <summary>
/// Represents a data size with support for bits, bytes, kilobytes, megabytes,
/// gigabytes, and terabytes. Provides formatting, arithmetic, and comparison operations.
/// </summary>
public struct DataSize :
    IComparable<DataSize>,
    IEquatable<DataSize>,
    IComparable,
    IFormattable
{
    public const string BitSymbol = "b";
    public const string ByteSymbol = "B";
    public const string KilobyteSymbol = "KB";
    public const string MegabyteSymbol = "MB";
    public const string GigabyteSymbol = "GB";
    public const string TerabyteSymbol = "TB";

    public const long BitsInByte = 8;
    public const long BytesInKilobyte = 1024;
    public const long BytesInMegabyte = 1_048_576;
    public const long BytesInGigabyte = 1_073_741_824;
    public const long BytesInTerabyte = 1_099_511_627_776;

    /// <summary>
    /// Gets the smallest possible <see cref="DataSize"/> value.
    /// </summary>
    public static readonly DataSize MinValue = FromBits(long.MinValue);

    /// <summary>
    /// Gets the largest possible <see cref="DataSize"/> value.
    /// </summary>
    public static readonly DataSize MaxValue = FromBits(long.MaxValue);

    /// <summary>
    /// Initializes a new instance of the <see cref="DataSize"/> struct with the specified number of bytes.
    /// </summary>
    /// <param name="byteSize">The size in bytes.</param>
    public DataSize(double byteSize)
    {
        Bits = (long)Math.Ceiling(byteSize * BitsInByte);
        Bytes = byteSize;
        Kilobytes = byteSize / BytesInKilobyte;
        Megabytes = byteSize / BytesInMegabyte;
        Gigabytes = byteSize / BytesInGigabyte;
        Terabytes = byteSize / BytesInTerabyte;
    }

    /// <summary>Gets the number of bits.</summary>
    public long Bits { get; private set; }

    /// <summary>Gets the number of bytes.</summary>
    public double Bytes { get; private set; }

    /// <summary>Gets the number of kilobytes.</summary>
    public double Kilobytes { get; private set; }

    /// <summary>Gets the number of megabytes.</summary>
    public double Megabytes { get; private set; }

    /// <summary>Gets the number of gigabytes.</summary>
    public double Gigabytes { get; private set; }

    /// <summary>Gets the number of terabytes.</summary>
    public double Terabytes { get; private set; }

    /// <summary>
    /// Gets the largest whole number value and its corresponding unit symbol.
    /// </summary>
    public string LargestWholeNumberSymbol
    {
        get
        {
            if (Math.Abs(Terabytes) >= 1) return TerabyteSymbol;
            if (Math.Abs(Gigabytes) >= 1) return GigabyteSymbol;
            if (Math.Abs(Megabytes) >= 1) return MegabyteSymbol;
            if (Math.Abs(Kilobytes) >= 1) return KilobyteSymbol;
            if (Math.Abs(Bytes) >= 1) return ByteSymbol;
            return BitSymbol;
        }
    }

    /// <summary>
    /// Gets the value in the largest whole number unit.
    /// </summary>
    public double LargestWholeNumberValue
    {
        get
        {
            if (Math.Abs(Terabytes) >= 1) return Terabytes;
            if (Math.Abs(Gigabytes) >= 1) return Gigabytes;
            if (Math.Abs(Megabytes) >= 1) return Megabytes;
            if (Math.Abs(Kilobytes) >= 1) return Kilobytes;
            if (Math.Abs(Bytes) >= 1) return Bytes;
            return Bits;
        }
    }

    // --- Factory methods ---

    /// <summary>Creates a <see cref="DataSize"/> from the specified number of bits.</summary>
    public static DataSize FromBits(long value) => new((double)value / BitsInByte);

    /// <summary>Creates a <see cref="DataSize"/> from the specified number of bytes.</summary>
    public static DataSize FromBytes(double value) => new(value);

    /// <summary>Creates a <see cref="DataSize"/> from the specified number of kilobytes.</summary>
    public static DataSize FromKilobytes(double value) => new(value * BytesInKilobyte);

    /// <summary>Creates a <see cref="DataSize"/> from the specified number of megabytes.</summary>
    public static DataSize FromMegabytes(double value) => new(value * BytesInMegabyte);

    /// <summary>Creates a <see cref="DataSize"/> from the specified number of gigabytes.</summary>
    public static DataSize FromGigabytes(double value) => new(value * BytesInGigabyte);

    /// <summary>Creates a <see cref="DataSize"/> from the specified number of terabytes.</summary>
    public static DataSize FromTerabytes(double value) => new(value * BytesInTerabyte);

    // --- Arithmetic operators ---

    public static DataSize operator +(DataSize a, DataSize b) => FromBytes(a.Bytes + b.Bytes);
    public static DataSize operator -(DataSize a, DataSize b) => FromBytes(a.Bytes - b.Bytes);
    public static DataSize operator *(DataSize a, double b) => FromBytes(a.Bytes * b);
    public static DataSize operator /(DataSize a, double b) => FromBytes(a.Bytes / b);
    public static bool operator ==(DataSize a, DataSize b) => a.Equals(b);
    public static bool operator !=(DataSize a, DataSize b) => !a.Equals(b);
    public static bool operator <(DataSize a, DataSize b) => a.Bits < b.Bits;
    public static bool operator >(DataSize a, DataSize b) => a.Bits > b.Bits;
    public static bool operator <=(DataSize a, DataSize b) => a.Bits <= b.Bits;
    public static bool operator >=(DataSize a, DataSize b) => a.Bits >= b.Bits;

    // --- IEquatable<DataSize> (correct implementation — positive finding) ---

    /// <inheritdoc />
    public bool Equals(DataSize other) => Bits == other.Bits;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DataSize other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => Bits.GetHashCode();

    // --- IComparable ---

    /// <inheritdoc />
    public int CompareTo(DataSize other) => Bits.CompareTo(other.Bits);

    /// <inheritdoc />
    public int CompareTo(object? obj) =>
        obj is DataSize other ? CompareTo(other) : throw new ArgumentException("Object is not a DataSize");

    // --- Formatting ---

    /// <summary>
    /// Returns a human-readable string representation using the largest whole number unit.
    /// </summary>
    public override string ToString() => ToString("0.##", CultureInfo.CurrentCulture);

    /// <summary>
    /// Returns a formatted string representation of this data size.
    /// </summary>
    /// <param name="format">A standard or custom numeric format string.</param>
    /// <param name="provider">An object that supplies culture-specific formatting information.</param>
    /// <returns>A formatted string representing this data size.</returns>
    public string ToString(string? format, IFormatProvider? formatProvider = null)
    {
        format ??= "0.## ";
        formatProvider ??= CultureInfo.CurrentCulture;

        if (!format.Contains('#') && !format.Contains('0'))
        {
            format = "0.## " + format;
        }

        // Anti-pattern: intermediate string allocation from Replace
        format = format.Replace("#.##", "0.##");

        var culture = formatProvider as CultureInfo ?? CultureInfo.CurrentCulture;

        bool has(string s) => culture.CompareInfo.IndexOf(format, s, CompareOptions.IgnoreCase) != -1;
        string output(double n) => n.ToString(format, formatProvider);

        // Anti-pattern: cascading if/Replace branches — each Replace allocates a new string
        if (has(TerabyteSymbol))
        {
            format = format.Replace(TerabyteSymbol, "TB");
            return output(Terabytes);
        }

        if (has(GigabyteSymbol))
        {
            format = format.Replace(GigabyteSymbol, "GB");
            return output(Gigabytes);
        }

        if (has(MegabyteSymbol))
        {
            format = format.Replace(MegabyteSymbol, "MB");
            return output(Megabytes);
        }

        if (has(KilobyteSymbol))
        {
            format = format.Replace(KilobyteSymbol, "KB");
            return output(Kilobytes);
        }

        // Correct: case-sensitive check with StringComparison.Ordinal
        if (format.Contains(ByteSymbol, StringComparison.Ordinal))
        {
            format = format.Replace(ByteSymbol, "B");
            return output(Bytes);
        }

        if (format.Contains(BitSymbol, StringComparison.Ordinal))
        {
            format = format.Replace(BitSymbol, "b");
            return output(Bits);
        }

        return $"{LargestWholeNumberValue.ToString(format, formatProvider)} {LargestWholeNumberSymbol}";
    }

    /// <summary>
    /// Parses a string representation into a <see cref="DataSize"/> value.
    /// Supports formats like "1.5 GB", "500 MB", "2TB", "8192b".
    /// </summary>
    /// <param name="input">The string to parse.</param>
    /// <returns>A <see cref="DataSize"/> value.</returns>
    /// <exception cref="FormatException">Thrown when the input cannot be parsed.</exception>
    public static DataSize Parse(string input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(input);

        input = input.Trim();

        // Try each unit suffix from largest to smallest
        if (TryParseWithSuffix(input, TerabyteSymbol, out var tb)) return FromTerabytes(tb);
        if (TryParseWithSuffix(input, GigabyteSymbol, out var gb)) return FromGigabytes(gb);
        if (TryParseWithSuffix(input, MegabyteSymbol, out var mb)) return FromMegabytes(mb);
        if (TryParseWithSuffix(input, KilobyteSymbol, out var kb)) return FromKilobytes(kb);
        if (TryParseWithSuffix(input, ByteSymbol, out var b)) return FromBytes(b);
        if (TryParseWithSuffix(input, BitSymbol, out var bits)) return FromBits((long)bits);

        if (double.TryParse(input, out var bytes))
        {
            return FromBytes(bytes);
        }

        throw new FormatException($"Unable to parse '{input}' as a data size.");
    }

    private static bool TryParseWithSuffix(string input, string suffix, out double value)
    {
        value = 0;
        if (input.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            var numericPart = input[..^suffix.Length].Trim();
            return double.TryParse(numericPart, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        return false;
    }
}
