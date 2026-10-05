using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace UnskipClosedTests.Tool;

internal static partial class InventoryEngine
{
    private sealed record ParsedFile(
        string Path,
        string FullPath,
        byte[] Bytes,
        string BlobOid,
        SyntaxTree Tree,
        CompilationUnitSyntax Root);

    private sealed record PendingCandidate(
        ParsedFile File,
        AttributeSyntax Attribute,
        string AttributeType,
        OwnerIdentity Owner,
        string StableOwnerId,
        List<IssueReference> References,
        List<string> StructuralDeferrals);

    public static Manifest Create(string requestedRoot, string? repositoryOverride, ToolConfig config)
    {
        GitRepository repository = GitRepository.Open(requestedRoot, repositoryOverride);
        List<ParsedFile> files = LoadFiles(repository, config);
        Dictionary<string, int> typeDeclarationCounts = CountTypeDeclarations(files);
        (
            HashSet<string> declaredTypes,
            HashSet<string> conditionalTypeNames
        ) = LoadCompilationContext(repository, files);
        List<PendingCandidate> pending = [];

        foreach (ParsedFile file in files)
        {
            foreach (AttributeSyntax attribute in file.Root.DescendantNodes().OfType<AttributeSyntax>())
            {
                string? attributeType = ResolveConfiguredAttributeType(
                    attribute,
                    config.IgnoreAttributeNames,
                    config.AttributeAliases,
                    declaredTypes,
                    conditionalTypeNames);
                if (attributeType is null)
                {
                    continue;
                }

                List<IssueReference> references = ExtractReferences(attribute, repository.Repository);
                if (references.Count == 0)
                {
                    continue;
                }

                if (attribute.FirstAncestorOrSelf<MethodDeclarationSyntax>() is MethodDeclarationSyntax method &&
                    method.AttributeLists.Any(list => list.Span.Contains(attribute.Span)))
                {
                    OwnerIdentity owner = CreateMethodOwner(
                        method, config, declaredTypes, conditionalTypeNames);
                    List<string> deferrals = [];
                    if (owner.TestFqns.Count == 0)
                    {
                        deferrals.Add("no_enumerated_tests");
                    }
                    if (HasGeneratedMarker(method))
                    {
                        deferrals.Add("generated_declaration");
                    }
                    if (HasConditionalCompilation(method))
                    {
                        deferrals.Add("method_has_conditional_compilation");
                    }

                    string stableOwnerId = StableOwnerId(repository.Repository, file.Path, owner, method);
                    pending.Add(new PendingCandidate(
                        file, attribute, attributeType, owner, stableOwnerId, references, deferrals));
                    continue;
                }

                if (attribute.FirstAncestorOrSelf<ClassDeclarationSyntax>() is ClassDeclarationSyntax type &&
                    type.AttributeLists.Any(list => list.Span.Contains(attribute.Span)))
                {
                    (OwnerIdentity owner, List<string> deferrals) =
                        CreateClassOwner(
                            type,
                            config,
                            typeDeclarationCounts,
                            declaredTypes,
                            conditionalTypeNames);
                    if (HasGeneratedMarker(type))
                    {
                        deferrals.Add("generated_declaration");
                    }

                    string stableOwnerId = StableOwnerId(repository.Repository, file.Path, owner, type);
                    pending.Add(new PendingCandidate(
                        file, attribute, attributeType, owner, stableOwnerId, references, deferrals));
                }
            }
        }

        List<Candidate> candidates = [];
        foreach (IGrouping<string, PendingCandidate> ownerGroup in pending
                     .OrderBy(static item => item.File.Path, StringComparer.Ordinal)
                     .ThenBy(static item => item.Attribute.SpanStart)
                     .GroupBy(static item => item.StableOwnerId, StringComparer.Ordinal))
        {
            int ordinal = 0;
            foreach (PendingCandidate item in ownerGroup)
            {
                ordinal++;
                FileLinePositionSpan lineSpan = item.Attribute.GetLocation().GetLineSpan();
                SourceSpan sourceSpan = new()
                {
                    Start = item.Attribute.SpanStart,
                    Length = item.Attribute.Span.Length,
                    StartLine = lineSpan.StartLinePosition.Line + 1,
                    StartColumn = lineSpan.StartLinePosition.Character + 1,
                    EndLine = lineSpan.EndLinePosition.Line + 1,
                    EndColumn = lineSpan.EndLinePosition.Character + 1,
                };
                string attributeText = item.File.Root.SyntaxTree.GetText().ToString(item.Attribute.Span);
                string candidateId = JsonSupport.Sha256(
                    $"candidate-v1\0{repository.Repository}\0{item.File.Path}\0{item.StableOwnerId}\0" +
                    $"{item.File.BlobOid}\0{sourceSpan.Start}:{sourceSpan.Length}\0{ordinal}");

                candidates.Add(new Candidate
                {
                    CandidateId = candidateId,
                    StableOwnerId = item.StableOwnerId,
                    Path = item.File.Path,
                    BlobOid = item.File.BlobOid,
                    SourceSha256 = JsonSupport.Sha256(item.File.Bytes),
                    AttributeSpan = sourceSpan,
                    AttributeTextSha256 = JsonSupport.Sha256(attributeText),
                    AttributeType = item.AttributeType,
                    Owner = item.Owner,
                    CanonicalIssueReferences = item.References,
                    Decision = new CandidateDecision
                    {
                        Eligible = false,
                        Deferrals = item.StructuralDeferrals.Order(StringComparer.Ordinal).ToList(),
                    },
                });
            }
        }

        candidates = candidates
            .OrderBy(static candidate => candidate.Path, StringComparer.Ordinal)
            .ThenBy(static candidate => candidate.AttributeSpan.Start)
            .ToList();
        Manifest manifest = new()
        {
            Repository = repository.Repository,
            SourceCommit = repository.Commit,
            GitObjectFormat = repository.ObjectFormat,
            ConfigDigest = ConfigLoader.Digest(config),
            CandidateCount = candidates.Count,
            Candidates = candidates,
        };
        manifest.ManifestDigest = JsonSupport.ManifestDigest(manifest);
        return manifest;
    }

    private static List<ParsedFile> LoadFiles(GitRepository repository, ToolConfig config)
    {
        Dictionary<string, string> paths = new(StringComparer.Ordinal);
        foreach (string configuredRoot in config.SourceRoots)
        {
            string normalizedRoot = PathRules.ValidateRelativePath(configuredRoot, "source root");
            string fullRoot = PathRules.ResolveInsideRoot(repository.Root, normalizedRoot, "source root");
            if (File.Exists(fullRoot))
            {
                if (!normalizedRoot.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ContractException($"Source root '{normalizedRoot}' is not a C# file.");
                }

                if (PathRules.MatchesAnyGlob(normalizedRoot, config.ExcludedGlobs.Concat(config.GeneratedGlobs)))
                {
                    throw new ContractException($"Explicit source root '{normalizedRoot}' is excluded or generated.");
                }

                paths[normalizedRoot] = fullRoot;
                continue;
            }

            if (!Directory.Exists(fullRoot))
            {
                continue;
            }

            PathRules.RejectReparsePoints(repository.Root, fullRoot);
            foreach (string file in EnumerateSourceFiles(repository.Root, fullRoot))
            {
                string extension = Path.GetExtension(file);
                if (!string.Equals(extension, ".cs", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string relative = Path.GetRelativePath(repository.Root, file).Replace('\\', '/');
                relative = PathRules.ValidateRelativePath(relative, "source path");
                if (PathRules.MatchesAnyGlob(relative, config.ExcludedGlobs.Concat(config.GeneratedGlobs)))
                {
                    continue;
                }

                paths[relative] = file;
            }
        }

        List<ParsedFile> result = [];
        foreach ((string relative, string fullPath) in paths.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            PathRules.RejectReparsePoints(repository.Root, fullPath);
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(fullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InfrastructureException($"Could not read source path '{relative}'.", ex);
            }

            string blobOid = repository.HeadBlobOid(relative);
            repository.RequireWorktreeMatchesHead(relative, bytes);
            string source;
            try
            {
                int offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                source = new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
            }
            catch (DecoderFallbackException ex)
            {
                throw new ContractException($"Source path '{relative}' is not valid UTF-8: {ex.Message}");
            }

            string prefix = source[..Math.Min(source.Length, 2048)];
            if (prefix.Contains("<auto-generated", StringComparison.OrdinalIgnoreCase) ||
                prefix.Contains("<autogenerated", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            SyntaxTree tree = CSharpSyntaxTree.ParseText(
                SourceText.From(source, Encoding.UTF8),
                new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse),
                relative);
            List<Diagnostic> errors = tree.GetDiagnostics()
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .ToList();
            if (errors.Count > 0)
            {
                throw new ContractException(
                    $"Source path '{relative}' has C# parse errors: {string.Join("; ", errors.Take(3))}");
            }

            result.Add(new ParsedFile(relative, fullPath, bytes, blobOid, tree, tree.GetCompilationUnitRoot()));
        }

        return result;
    }

    private static IEnumerable<string> EnumerateSourceFiles(
        string repositoryRoot,
        string sourceRoot)
    {
        Stack<string> pending = new();
        pending.Push(sourceRoot);
        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            PathRules.RejectReparsePoints(repositoryRoot, directory);

            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(
                    directory,
                    "*",
                    SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new InfrastructureException(
                    $"Could not enumerate source directory '{directory}'.",
                    ex);
            }

            foreach (string entry in entries)
            {
                FileAttributes attributes;
                try
                {
                    attributes = File.GetAttributes(entry);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    throw new InfrastructureException(
                        $"Could not inspect source entry '{entry}'.",
                        ex);
                }

                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new ContractException(
                        $"Source path '{entry}' traverses a symlink or reparse point.");
                }

                if (attributes.HasFlag(FileAttributes.Directory))
                {
                    pending.Push(entry);
                }
                else
                {
                    yield return entry;
                }
            }
        }
    }

    private static Dictionary<string, int> CountTypeDeclarations(IEnumerable<ParsedFile> files)
    {
        Dictionary<string, int> counts = new(StringComparer.Ordinal);
        foreach (TypeDeclarationSyntax declaration in files.SelectMany(static file =>
                     file.Root.DescendantNodes().OfType<TypeDeclarationSyntax>()))
        {
            string fqn = TypeFqn(declaration);
            counts[fqn] = counts.GetValueOrDefault(fqn) + 1;
        }

        return counts;
    }

    private static (
        HashSet<string> DeclaredTypes,
        HashSet<string> ConditionalTypeNames)
        LoadCompilationContext(GitRepository repository, IReadOnlyCollection<ParsedFile> scannedFiles)
    {
        Dictionary<string, CompilationUnitSyntax> roots = scannedFiles.ToDictionary(
            static file => file.Path,
            static file => file.Root,
            StringComparer.Ordinal);
        HashSet<string> conditionalTypeNames = new(StringComparer.Ordinal);
        foreach (string path in repository.TrackedCSharpPaths())
        {
            string text = DecodeTrackedCSharpSource(repository.HeadBytes(path), path);

            if (ConditionalDirectiveRegex().IsMatch(text))
            {
                foreach (Match match in RawTypeDeclarationRegex().Matches(text))
                {
                    conditionalTypeNames.Add(match.Groups["name"].Value);
                }
            }

            if (roots.ContainsKey(path))
            {
                continue;
            }

            SyntaxTree tree = CSharpSyntaxTree.ParseText(
                text,
                new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse),
                path);
            if (!tree.GetDiagnostics().Any(static diagnostic =>
                    diagnostic.Severity == DiagnosticSeverity.Error))
            {
                roots[path] = tree.GetCompilationUnitRoot();
            }
        }

        HashSet<string> declaredTypes = roots.Values
            .SelectMany(static root => root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            .Select(TypeFqn)
            .ToHashSet(StringComparer.Ordinal);
        return (declaredTypes, conditionalTypeNames);
    }

    private static string DecodeTrackedCSharpSource(byte[] bytes, string path)
    {
        try
        {
            if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE, 0x00, 0x00 }))
            {
                return new UTF32Encoding(false, false, true).GetString(bytes, 4, bytes.Length - 4);
            }

            if (bytes.AsSpan().StartsWith(new byte[] { 0x00, 0x00, 0xFE, 0xFF }))
            {
                return new UTF32Encoding(true, false, true).GetString(bytes, 4, bytes.Length - 4);
            }

            if (bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
            {
                return new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3);
            }

            if (bytes.AsSpan().StartsWith(new byte[] { 0xFF, 0xFE }))
            {
                return new UnicodeEncoding(false, false, true).GetString(bytes, 2, bytes.Length - 2);
            }

            if (bytes.AsSpan().StartsWith(new byte[] { 0xFE, 0xFF }))
            {
                return new UnicodeEncoding(true, false, true).GetString(bytes, 2, bytes.Length - 2);
            }

            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException ex)
        {
            throw new ContractException(
                $"Tracked C# path '{path}' uses an unsupported or malformed encoding: {ex.Message}");
        }
    }

    private static OwnerIdentity CreateMethodOwner(
        MethodDeclarationSyntax method,
        ToolConfig config,
        IReadOnlySet<string> declaredTypes,
        IReadOnlySet<string> conditionalTypeNames)
    {
        TypeDeclarationSyntax? type = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (type is null)
        {
            throw new ContractException("An Ignore attribute on a method has no actual containing type.");
        }

        string typeFqn = TypeFqn(type);
        string signature = MethodSignature(method);
        bool isTest = method.AttributeLists.SelectMany(static list => list.Attributes)
            .Any(attribute => ResolveConfiguredAttributeType(
                attribute,
                config.TestAttributeNames,
                config.AttributeAliases,
                declaredTypes,
                conditionalTypeNames) is not null);
        return new OwnerIdentity
        {
            Kind = "method",
            Namespace = NamespaceName(type),
            ContainingTypes = ContainingTypeNames(type),
            TypeFqn = typeFqn,
            DeclarationId = MethodDeclarationId(method),
            MethodName = method.Identifier.ValueText,
            MethodSignature = signature,
            TestFqns = isTest ? [$"{typeFqn}.{method.Identifier.ValueText}"] : [],
        };
    }

    private static (OwnerIdentity Owner, List<string> Deferrals) CreateClassOwner(
        ClassDeclarationSyntax type,
        ToolConfig config,
        IReadOnlyDictionary<string, int> declarationCounts,
        IReadOnlySet<string> declaredTypes,
        IReadOnlySet<string> conditionalTypeNames)
    {
        string typeFqn = TypeFqn(type);
        List<string> deferrals = [];
        List<string> containingTypes = ContainingTypeNames(type);
        if (containingTypes.Count > 1)
        {
            deferrals.Add("class_is_nested");
        }

        if (type.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            deferrals.Add("class_is_partial");
        }

        if (type.BaseList is not null && type.BaseList.Types.Count > 0)
        {
            deferrals.Add("class_has_base_types");
        }

        if (declarationCounts.GetValueOrDefault(typeFqn) != 1)
        {
            deferrals.Add("duplicate_type_declarations");
        }

        if (HasConditionalCompilation(type))
        {
            deferrals.Add("class_has_conditional_compilation");
        }

        List<MethodDeclarationSyntax> attributedMethods = type.Members
            .OfType<MethodDeclarationSyntax>()
            .Where(static method => method.AttributeLists.Count > 0)
            .ToList();
        List<MethodDeclarationSyntax> recognizedTests = attributedMethods
            .Where(method => method.AttributeLists.SelectMany(static list => list.Attributes)
                .Any(attribute => ResolveConfiguredAttributeType(
                    attribute,
                    config.TestAttributeNames,
                    config.AttributeAliases,
                    declaredTypes,
                    conditionalTypeNames) is not null))
            .ToList();
        List<string> tests = recognizedTests
            .Select(method => $"{typeFqn}.{method.Identifier.ValueText}")
            .Order(StringComparer.Ordinal)
            .ToList();
        if (attributedMethods.Except(recognizedTests).Any())
        {
            deferrals.Add("class_has_unclassified_attributed_methods");
        }
        if (tests.Count == 0)
        {
            deferrals.Add("no_enumerated_tests");
        }

        if (tests.Distinct(StringComparer.Ordinal).Count() != tests.Count)
        {
            deferrals.Add("ambiguous_test_fqns");
        }

        OwnerIdentity owner = new()
        {
            Kind = "class",
            Namespace = NamespaceName(type),
            ContainingTypes = containingTypes,
            TypeFqn = typeFqn,
            DeclarationId = $"T:{typeFqn}",
            TestFqns = tests.Distinct(StringComparer.Ordinal).ToList(),
        };
        return (owner, deferrals.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList());
    }

    private static bool HasConditionalCompilation(SyntaxNode node) =>
        node.DescendantTrivia(descendIntoTrivia: true).Any(static trivia =>
            trivia.IsKind(SyntaxKind.IfDirectiveTrivia) ||
            trivia.IsKind(SyntaxKind.ElifDirectiveTrivia) ||
            trivia.IsKind(SyntaxKind.ElseDirectiveTrivia) ||
            trivia.IsKind(SyntaxKind.EndIfDirectiveTrivia));

    private static string StableOwnerId(
        string repository,
        string path,
        OwnerIdentity owner,
        MemberDeclarationSyntax declaration)
    {
        int declarationOrdinal = declaration switch
        {
            MethodDeclarationSyntax method => method.SyntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(candidate => string.Equals(
                    MethodDeclarationId(candidate),
                    owner.DeclarationId,
                    StringComparison.Ordinal))
                .Count(candidate => candidate.SpanStart < method.SpanStart) + 1,
            TypeDeclarationSyntax type => type.SyntaxTree.GetRoot()
                .DescendantNodes()
                .OfType<TypeDeclarationSyntax>()
                .Where(candidate => string.Equals(
                    $"T:{TypeFqn(candidate)}",
                    owner.DeclarationId,
                    StringComparison.Ordinal))
                .Count(candidate => candidate.SpanStart < type.SpanStart) + 1,
            _ => throw new ContractException("Unsupported owner declaration kind."),
        };
        return JsonSupport.Sha256(
            $"owner-v1\0{repository}\0{path}\0{owner.DeclarationId}\0{declarationOrdinal}");
    }

    private static bool HasGeneratedMarker(MemberDeclarationSyntax declaration) =>
        HasDirectGeneratedMarker(declaration) ||
        declaration.Ancestors().OfType<TypeDeclarationSyntax>().Any(HasDirectGeneratedMarker);

    private static bool HasDirectGeneratedMarker(MemberDeclarationSyntax declaration) =>
        declaration.AttributeLists
            .SelectMany(static list => list.Attributes)
            .Any(static attribute =>
            {
                string name = attribute.Name.WithoutTrivia().ToFullString()
                    .Replace("global::", "", StringComparison.Ordinal)
                    .Split('.')
                    .Last();
                if (name.EndsWith("Attribute", StringComparison.Ordinal))
                {
                    name = name[..^"Attribute".Length];
                }

                return name is "GeneratedCode" or "CompilerGenerated";
            });

    private static string MethodSignature(MethodDeclarationSyntax method)
    {
        string explicitInterface = method.ExplicitInterfaceSpecifier is null
            ? ""
            : $"{method.ExplicitInterfaceSpecifier.Name.WithoutTrivia().ToFullString()}.";
        string arity = method.TypeParameterList is null ? "" : $"`{method.TypeParameterList.Parameters.Count}";
        string parameters = string.Join(",",
            method.ParameterList.Parameters.Select(static parameter =>
                $"{parameter.Modifiers.ToFullString().Trim()}:{parameter.Type?.WithoutTrivia().ToFullString() ?? "?"}"));
        return $"{explicitInterface}{method.Identifier.ValueText}{arity}({parameters})";
    }

    private static string MethodDeclarationId(MethodDeclarationSyntax method)
    {
        TypeDeclarationSyntax? type = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (type is null)
        {
            throw new ContractException("A method owner has no actual containing type.");
        }

        return $"M:{TypeFqn(type)}.{MethodSignature(method)}";
    }

    private static string TypeFqn(TypeDeclarationSyntax type)
    {
        List<string> parts = [];
        string namespaceName = NamespaceName(type);
        if (namespaceName.Length > 0)
        {
            parts.Add(namespaceName);
        }

        parts.AddRange(ContainingTypeNames(type));
        return string.Join('.', parts);
    }

    private static string NamespaceName(SyntaxNode node) =>
        string.Join('.',
            node.Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Reverse()
                .Select(static declaration => declaration.Name.WithoutTrivia().ToFullString()));

    private static List<string> ContainingTypeNames(TypeDeclarationSyntax type) =>
        type.AncestorsAndSelf()
            .OfType<TypeDeclarationSyntax>()
            .Reverse()
            .Select(static declaration =>
                declaration.TypeParameterList is null
                    ? declaration.Identifier.ValueText
                    : $"{declaration.Identifier.ValueText}`{declaration.TypeParameterList.Parameters.Count}")
            .ToList();

    internal static string? ResolveConfiguredAttributeType(
        AttributeSyntax attribute,
        IEnumerable<string> configuredNames,
        IReadOnlyDictionary<string, string> configuredAliases,
        IReadOnlySet<string> declaredTypes,
        IReadOnlySet<string> conditionalTypeNames)
    {
        List<string> configuredTypes = configuredNames
            .Select(NormalizeAttributeType)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        configuredTypes.RemoveAll(declaredTypes.Contains);
        configuredTypes.RemoveAll(configured =>
            conditionalTypeNames.Contains(configured.Split('.').Last()));
        if (configuredTypes.Count == 0)
        {
            return null;
        }
        string actual = attribute.Name.WithoutTrivia().ToFullString();
        if (actual.StartsWith("global::", StringComparison.Ordinal))
        {
            string globalType = NormalizeAttributeType(actual["global::".Length..]);
            return configuredTypes.Contains(globalType, StringComparer.Ordinal)
                ? globalType
                : null;
        }

        IReadOnlyList<UsingDirectiveSyntax> usings = VisibleUsings(attribute);
        List<IGrouping<string, UsingDirectiveSyntax>> aliasGroups = usings
            .Where(static directive => directive.Alias is not null)
            .GroupBy(
                static directive => directive.Alias!.Name.Identifier.ValueText,
                StringComparer.Ordinal)
            .ToList();
        HashSet<string> sourceAliasNames = aliasGroups
            .Select(static group => group.Key)
            .ToHashSet(StringComparer.Ordinal);
        Dictionary<string, string> aliases = aliasGroups
            .Where(group => group
                .Select(static directive => directive.Name!.WithoutTrivia().ToFullString())
                .Distinct(StringComparer.Ordinal)
                .Count() == 1)
            .ToDictionary(
                static group => group.Key,
                static group => group.First().Name!.WithoutTrivia().ToFullString(),
                StringComparer.Ordinal);
        if (!actual.Contains('.', StringComparison.Ordinal))
        {
            string simpleName = NormalizeAttributeSimpleName(actual);
            if (declaredTypes.Any(declared =>
                    string.Equals(
                        declared.Split('.').Last(),
                        simpleName,
                        StringComparison.Ordinal)))
            {
                return null;
            }
        }

        if (sourceAliasNames.Contains(actual))
        {
            if (!aliases.TryGetValue(actual, out string? aliasedType))
            {
                return null;
            }
            string normalizedAlias = NormalizeAttributeType(aliasedType);
            return configuredTypes.Contains(normalizedAlias, StringComparer.Ordinal)
                ? normalizedAlias
                : null;
        }

        if (configuredAliases.TryGetValue(actual, out string? configuredAliasType))
        {
            string normalizedConfiguredAlias = NormalizeAttributeType(configuredAliasType);
            return configuredTypes.Contains(normalizedConfiguredAlias, StringComparer.Ordinal)
                ? normalizedConfiguredAlias
                : null;
        }

        if (actual.Contains('.', StringComparison.Ordinal))
        {
            string[] parts = actual.Split('.');
            if (aliases.TryGetValue(parts[0], out string? aliasedNamespace))
            {
                actual = string.Join('.', [aliasedNamespace, .. parts.Skip(1)]);
            }

            string qualifiedType = NormalizeAttributeType(actual);
            return configuredTypes.Contains(qualifiedType, StringComparer.Ordinal)
                ? qualifiedType
                : null;
        }

        return null;
    }

    private static string NormalizeAttributeType(string value)
    {
        string[] parts = value.Split('.');
        parts[^1] = NormalizeAttributeSimpleName(parts[^1]);
        return string.Join('.', parts);
    }

    private static string NormalizeAttributeSimpleName(string value) =>
        value.EndsWith("Attribute", StringComparison.Ordinal)
            ? value
            : $"{value}Attribute";

    private static IReadOnlyList<UsingDirectiveSyntax> VisibleUsings(SyntaxNode node) =>
        node.SyntaxTree.GetCompilationUnitRoot().Usings
            .Concat(node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>()
                .Reverse()
                .SelectMany(static declaration => declaration.Usings))
            .ToList();

    [GeneratedRegex(
        @"(?m)^[ \t]*#(?:if|elif)\b",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex ConditionalDirectiveRegex();

    [GeneratedRegex(
        @"\b(?:class|struct|record(?:\s+class|\s+struct)?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex RawTypeDeclarationRegex();

    private static List<IssueReference> ExtractReferences(AttributeSyntax attribute, string currentRepository)
    {
        List<IssueReference> references = [];
        if (attribute.ArgumentList is null)
        {
            return references;
        }

        foreach (AttributeArgumentSyntax argument in attribute.ArgumentList.Arguments)
        {
            bool supportedName = argument.NameEquals is null ||
                string.Equals(argument.NameEquals.Name.Identifier.ValueText, "IgnoreMessage", StringComparison.Ordinal);
            if (!supportedName || argument.NameColon is not null ||
                argument.Expression is not LiteralExpressionSyntax literal ||
                !literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                continue;
            }

            string value = literal.Token.ValueText;
            references.AddRange(ParseReferences(value, currentRepository));
        }

        return references
            .GroupBy(static reference => reference.Canonical, StringComparer.Ordinal)
            .Select(static group => group.First())
            .OrderBy(static reference => reference.Canonical, StringComparer.Ordinal)
            .ToList();
    }

    private static IEnumerable<IssueReference> ParseReferences(string value, string currentRepository)
    {
        List<(int Start, int Length, string Owner, string Repo, int Number, string Kind)> matches = [];
        foreach (Match match in FullReferenceRegex().Matches(value))
        {
            matches.Add((
                match.Index,
                match.Length,
                match.Groups["owner"].Value,
                match.Groups["repo"].Value,
                int.Parse(match.Groups["number"].Value, System.Globalization.CultureInfo.InvariantCulture),
                match.Groups["kind"].Value.Equals("pull", StringComparison.OrdinalIgnoreCase) ? "pull_request" : "issue"));
        }

        foreach (Match match in QualifiedReferenceRegex().Matches(value))
        {
            if (matches.Any(existing => RangesOverlap(existing.Start, existing.Length, match.Index, match.Length)))
            {
                continue;
            }

            matches.Add((
                match.Index,
                match.Length,
                match.Groups["owner"].Value,
                match.Groups["repo"].Value,
                int.Parse(match.Groups["number"].Value, System.Globalization.CultureInfo.InvariantCulture),
                "unknown"));
        }

        string[] current = currentRepository.Split('/');
        foreach (Match match in BareReferenceRegex().Matches(value))
        {
            if (matches.Any(existing => RangesOverlap(existing.Start, existing.Length, match.Index, match.Length)))
            {
                continue;
            }

            matches.Add((
                match.Index,
                match.Length,
                current[0],
                current[1],
                int.Parse(match.Groups["number"].Value, System.Globalization.CultureInfo.InvariantCulture),
                "unknown"));
        }

        foreach ((_, _, string ownerValue, string repoValue, int number, string kind) in matches.OrderBy(static item => item.Start))
        {
            if (number <= 0)
            {
                continue;
            }

            string owner = ownerValue.ToLowerInvariant();
            string repo = repoValue.ToLowerInvariant();
            string pathKind = kind == "pull_request" ? "pull" : "issues";
            yield return new IssueReference
            {
                Kind = kind,
                Owner = owner,
                Repo = repo,
                Number = number,
                Canonical = $"{owner}/{repo}#{number}",
                Url = $"https://github.com/{owner}/{repo}/{pathKind}/{number}",
                Eligibility = false,
                State = "unknown",
                StateReason = "unresolved",
            };
        }
    }

    private static bool RangesOverlap(int firstStart, int firstLength, int secondStart, int secondLength) =>
        firstStart < secondStart + secondLength && secondStart < firstStart + firstLength;

    [GeneratedRegex(
        @"https://github\.com/(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)/(?<kind>issues|pull)/(?<number>[1-9][0-9]*)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
    private static partial Regex FullReferenceRegex();

    [GeneratedRegex(
        @"(?<![A-Za-z0-9_.-])(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)#(?<number>[1-9][0-9]*)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex QualifiedReferenceRegex();

    [GeneratedRegex(
        @"(?<![A-Za-z0-9_/#])#(?<number>[1-9][0-9]*)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex BareReferenceRegex();
}
