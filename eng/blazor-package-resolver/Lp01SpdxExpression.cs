using System.Text;
using System.Text.RegularExpressions;

namespace BlazorPackageResolver;

internal sealed class SpdxSyntaxException : Exception;
internal sealed class SpdxLimitException : Exception;

internal sealed class Lp01SpdxExpression
{
    private static readonly Regex Token = new(
        @"\G(?:(?:DocumentRef-[A-Za-z0-9.-]+:)?LicenseRef-[A-Za-z0-9.-]+|[A-Za-z0-9][A-Za-z0-9.+-]*|[()])",
        RegexOptions.CultureInvariant);
    private static readonly Regex ExceptionId = new(
        @"\A[A-Za-z0-9][A-Za-z0-9.+-]*\z", RegexOptions.CultureInvariant);
    private readonly string[] tokens;
    private int position;

    private sealed record Node(string Kind, string Id, IReadOnlyList<Node> Children)
    {
        internal static Node Leaf(string kind, string id) => new(kind, id, []);
    }

    private readonly record struct Parsed(int Alternatives, Node Tree);
    internal IReadOnlyList<HashSet<string>> Alternatives { get; }
    internal IReadOnlySet<string> LicenseIds { get; }
    internal IReadOnlySet<string> Terms { get; }
    internal string Canonical { get; }

    private Lp01SpdxExpression(string[] tokens)
    {
        this.tokens = tokens;
        if (tokens.Length == 0) throw new SpdxSyntaxException();
        var parsed = ParseOr(0);
        if (position != tokens.Length) throw new SpdxSyntaxException();
        var tree = Normalize(parsed.Tree);
        Canonical = Render(tree, null);
        LicenseIds = Leaves(tree).Select(n => n.Id).ToHashSet(StringComparer.Ordinal);
        Terms = Leaves(tree).Select(n => n.Kind == "WITH" ? "WITH:" + n.Children[0].Id : n.Id)
            .Concat(Leaves(tree).Select(n => n.Id)).ToHashSet(StringComparer.Ordinal);
        Alternatives = Expand(parsed.Tree);
    }

    internal static Lp01SpdxExpression Parse(string text)
    {
        if (text.Length > 4096) throw new SpdxLimitException();
        try
        {
            if (new UTF8Encoding(false, true).GetByteCount(text) > 4096)
                throw new SpdxLimitException();
        }
        catch (EncoderFallbackException) { throw new SpdxSyntaxException(); }
        if (text.Any(c => !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or
                >= '0' and <= '9' or '.' or '+' or '-' or '(' or ')' or ':' or
                ' ' or '\t' or '\r' or '\n')))
            throw new SpdxSyntaxException();
        var tokens = new List<string>();
        for (var index = 0; index < text.Length;)
        {
            if (text[index] is ' ' or '\t' or '\r' or '\n') { index++; continue; }
            var match = Token.Match(text, index);
            if (!match.Success) throw new SpdxSyntaxException();
            tokens.Add(match.Value);
            if (tokens.Count > 255) throw new SpdxLimitException();
            index += match.Length;
        }
        return new Lp01SpdxExpression(tokens.ToArray());
    }

    private Parsed ParseAtom(int depth)
    {
        if (position >= tokens.Length) throw new SpdxSyntaxException();
        var term = tokens[position++];
        if (term == "(")
        {
            if (depth >= 16) throw new SpdxLimitException();
            var value = ParseOr(depth + 1);
            if (position >= tokens.Length || tokens[position++] != ")")
                throw new SpdxSyntaxException();
            return value;
        }
        if (term is "AND" or "OR" or "WITH" or ")" || !IsTerm(term))
            throw new SpdxSyntaxException();
        if (position < tokens.Length && tokens[position] == "WITH")
        {
            position++;
            if (position >= tokens.Length || !ExceptionId.IsMatch(tokens[position]) ||
                tokens[position] is "AND" or "OR" or "WITH")
                throw new SpdxSyntaxException();
            return new(1, new Node("WITH", term, [Node.Leaf("EXCEPTION", tokens[position++])]));
        }
        return new(1, Node.Leaf("ID", term));
    }

    private Parsed ParseAnd(int depth)
    {
        var left = ParseAtom(depth);
        while (position < tokens.Length && tokens[position] == "AND")
        {
            position++;
            var right = ParseAtom(depth);
            if (left.Alternatives * right.Alternatives > 128) throw new SpdxLimitException();
            left = new(left.Alternatives * right.Alternatives,
                new Node("AND", "", [left.Tree, right.Tree]));
        }
        return left;
    }

    private Parsed ParseOr(int depth)
    {
        var left = ParseAnd(depth);
        while (position < tokens.Length && tokens[position] == "OR")
        {
            position++;
            var right = ParseAnd(depth);
            if (left.Alternatives + right.Alternatives > 128) throw new SpdxLimitException();
            left = new(left.Alternatives + right.Alternatives,
                new Node("OR", "", [left.Tree, right.Tree]));
        }
        return left;
    }

    private static bool IsTerm(string value) =>
        value.StartsWith("LicenseRef-", StringComparison.Ordinal) ||
        value.StartsWith("DocumentRef-", StringComparison.Ordinal) ||
        ExceptionId.IsMatch(value);

    private static Node Normalize(Node node)
    {
        if (node.Kind is "ID" or "WITH") return node;
        var children = node.Children.Select(Normalize)
            .SelectMany(child => child.Kind == node.Kind ? child.Children : [child])
            .DistinctBy(Key, StringComparer.Ordinal).OrderBy(Key, StringComparer.Ordinal).ToArray();
        return children.Length == 1 ? children[0] : new(node.Kind, "", children);
    }

    private static string Key(Node node) => node.Kind switch
    {
        "ID" => "ID:" + node.Id,
        "WITH" => "WITH:" + node.Id + ":" + node.Children[0].Id,
        _ => node.Kind + "(" + string.Join(",", node.Children.Select(Key)) + ")"
    };

    private static string Render(Node node, string? parent) => node.Kind switch
    {
        "ID" => node.Id,
        "WITH" => node.Id + " WITH " + node.Children[0].Id,
        _ => (parent == "AND" && node.Kind == "OR" ? "(" : "") +
             string.Join(" " + node.Kind + " ", node.Children.Select(c => Render(c, node.Kind))) +
             (parent == "AND" && node.Kind == "OR" ? ")" : "")
    };

    private static IEnumerable<Node> Leaves(Node node) =>
        node.Kind is "ID" or "WITH" ? [node] : node.Children.SelectMany(Leaves);

    private static IReadOnlyList<HashSet<string>> Expand(Node node)
    {
        if (node.Kind == "ID") return [new HashSet<string>([node.Id], StringComparer.Ordinal)];
        if (node.Kind == "WITH")
            return [new HashSet<string>([node.Id, "WITH:" + node.Children[0].Id],
                StringComparer.Ordinal)];
        if (node.Kind == "OR") return node.Children.SelectMany(Expand).ToArray();
        var options = new List<HashSet<string>> { new(StringComparer.Ordinal) };
        foreach (var child in node.Children)
            options = (from left in options from right in Expand(child)
                select new HashSet<string>(left.Concat(right), StringComparer.Ordinal)).ToList();
        return options;
    }
}
