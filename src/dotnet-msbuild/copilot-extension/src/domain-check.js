// MSBuild domain relevance detection.
// Mirrors the logic from src/dotnet-msbuild/skills/shared/domain-check.md

// High-confidence signals — any one of these means MSBuild context
const HIGH_CONFIDENCE_PATTERNS = [
  // Error code prefixes
  /\bCS\d{4}\b/, // C# compiler
  /\bMSB\d{4}\b/, // MSBuild engine
  /\bNU\d{4}\b/, // NuGet
  /\bNETSDK\d{4}\b/, // .NET SDK
  /\bBC\d{4}\b/, // Visual Basic compiler
  /\bFS\d{4}\b/, // F# compiler
  /\bAD\d{4}\b/, // Analyzer diagnostics

  // File extensions
  /\.csproj\b/i,
  /\.vbproj\b/i,
  /\.fsproj\b/i,
  /\.sln\b/i,
  /\.slnx\b/i,
  /\.props\b/i,
  /\.targets\b/i,
  /\.binlog\b/i,
  /\.nupkg\b/i,

  // CLI commands
  /\bdotnet\s+(build|test|pack|publish|restore|run|new|clean)\b/i,
  /\bmsbuild(\.exe)?\b/i,

  // MSBuild XML elements
  /\bSdk\s*=\s*"Microsoft\.NET\.Sdk/i,
  /<PackageReference\b/i,
  /<ProjectReference\b/i,
  /<PropertyGroup\b/i,
  /<ItemGroup\b/i,
  /<Target\b/i,

  // Key files
  /\bDirectory\.Build\.props\b/i,
  /\bDirectory\.Build\.targets\b/i,
  /\bDirectory\.Packages\.props\b/i,
  /\bglobal\.json\b/i,
  /\bnuget\.config\b/i,
];

// Medium-confidence signals — need at least 2 or combined with context
const MEDIUM_CONFIDENCE_PATTERNS = [
  /\b\.NET\b/,
  /\bNuGet\b/i,
  /\bC#\b/i,
  /\bcsproj\b/i,
  /\bVisual Studio\b/i,
  /\bsolution\b/i,
  /\bassembly\b/i,
  /\bpackage\s+reference\b/i,
  /\btarget\s+framework\b/i,
  /\bTFM\b/,
];

// Negative signals — strongly suggest non-MSBuild context
const NEGATIVE_PATTERNS = [
  /\bpackage\.json\b/i,
  /\bnode_modules\b/i,
  /\bnpm\s+(install|run|test)\b/i,
  /\byarn\s+(add|install|run)\b/i,
  /\bwebpack\b/i,
  /\bCargo\.toml\b/i,
  /\bcargo\s+(build|test|run)\b/i,
  /\brustc\b/i,
  /\bpom\.xml\b/i,
  /\bbuild\.gradle\b/i,
  /\bMakefile\b/,
  /\bCMakeLists\.txt\b/i,
  /\bgo\.(mod|sum)\b/i,
  /\bpyproject\.toml\b/i,
  /\bsetup\.py\b/i,
  /\bpip\s+install\b/i,
];

/**
 * Check if a user message is related to MSBuild / .NET builds.
 * Returns true if MSBuild skills should activate.
 */
function isMSBuildRelated(message) {
  if (!message || typeof message !== "string") return false;

  // Check for negative signals first
  const negativeCount = NEGATIVE_PATTERNS.filter((p) => p.test(message)).length;

  // Check for high-confidence signals
  const highConfidence = HIGH_CONFIDENCE_PATTERNS.some((p) => p.test(message));
  if (highConfidence) return true;

  // If only negative signals, not MSBuild
  if (negativeCount > 0) return false;

  // Check for medium-confidence signals — need at least 2
  const mediumCount = MEDIUM_CONFIDENCE_PATTERNS.filter((p) =>
    p.test(message)
  ).length;
  return mediumCount >= 2;
}

module.exports = { isMSBuildRelated };
