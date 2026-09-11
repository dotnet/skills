using System.Collections.Immutable;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class AnalyzerThree : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule =
        new("CONTOSO003", "Fixture analyzer", "Fixture analyzer", "Performance",
            DiagnosticSeverity.Hidden, isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationAction(_ => Thread.Sleep(75));
    }
}
