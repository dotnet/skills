using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AnalyzerTwo : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule =
        new("CONTOSO002", "Fixture analyzer", "Fixture analyzer", "Performance",
            DiagnosticSeverity.Hidden, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationAction(_ => Thread.Sleep(75));
    }
}
