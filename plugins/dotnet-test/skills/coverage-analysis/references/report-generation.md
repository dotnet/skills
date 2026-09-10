# Coverage Analysis — ReportGenerator HTML/CSV reports

Read this file **only** when the user explicitly asked for HTML/CSV reports and
the direct coverage/risk answer has already been delivered.

## Verify or install ReportGenerator

```powershell
$rgAvailable = $false
$rgCommand = Get-Command reportgenerator -ErrorAction SilentlyContinue
if ($rgCommand) {
    $rgAvailable = $true
    Write-Host "RG_INSTALLED:already-present"
} else {
    $rgToolPath = Join-Path "<COVERAGE_DIR>" ".tools"
    dotnet tool install dotnet-reportgenerator-globaltool --tool-path $rgToolPath
    if ($LASTEXITCODE -eq 0) {
        $env:PATH = "$rgToolPath$([System.IO.Path]::PathSeparator)$env:PATH"
        $rgCommand = Get-Command reportgenerator -ErrorAction SilentlyContinue
        if ($rgCommand) {
            $rgAvailable = $true
            Write-Host "RG_INSTALLED:true (tool-path: $rgToolPath)"
        } else {
            Write-Host "RG_INSTALLED:false"
            Write-Host "RG_INSTALL_ERROR:reportgenerator-not-available"
        }
    } else {
        Write-Host "RG_INSTALLED:false"
        Write-Host "RG_INSTALL_ERROR:reportgenerator-not-available"
    }
}
Write-Host "RG_AVAILABLE:$rgAvailable"
```

If installation fails (no internet), keep `RG_AVAILABLE:false`, leave the existing user-facing summary as the final output, and note that HTML reports were skipped.

## Step 7: Generate HTML/CSV reports

```powershell
$reportsDir = Join-Path "<COVERAGE_DIR>" "reports"
if ($rgAvailable) {
    reportgenerator `
        -reports:"<semicolon-separated COBERTURA paths>" `
        -targetdir:$reportsDir `
        -reporttypes:"Html;TextSummary;MarkdownSummaryGithub;CsvSummary" `
        -title:"Coverage Report" `
        -tag:"coverage-analysis-skill"

    Get-Content (Join-Path $reportsDir "Summary.txt") -ErrorAction SilentlyContinue
} else {
    Write-Host "REPORTGENERATOR_SKIPPED:true"
}
```

After report generation completes successfully, you may follow up with a short
message pointing the user to the generated HTML report (one paragraph, no need
to repeat the summary).
