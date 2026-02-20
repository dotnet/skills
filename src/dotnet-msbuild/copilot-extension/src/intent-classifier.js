// Intent classification for MSBuild queries.
// Maps user messages to knowledge areas for context-appropriate responses.

const INTENTS = {
  BUILD_ERROR: {
    knowledgeKey: "build-errors",
    description: "Build failure diagnosis",
  },
  PERFORMANCE: {
    knowledgeKey: "performance",
    description: "Build performance optimization",
  },
  STYLE_REVIEW: {
    knowledgeKey: "style-guide",
    description: "Project file quality and anti-patterns",
  },
  MODERNIZATION: {
    knowledgeKey: "modernization",
    description: "Legacy project modernization",
  },
  GENERAL: {
    knowledgeKey: null,
    description: "General MSBuild question",
  },
};

// Pattern groups for intent classification
const INTENT_PATTERNS = {
  BUILD_ERROR: [
    /\berror\b/i,
    /\bfail(ed|ure|s|ing)?\b/i,
    /\bCS\d{4}\b/,
    /\bMSB\d{4}\b/,
    /\bNU\d{4}\b/,
    /\bNETSDK\d{4}\b/,
    /\bAD\d{4}\b/,
    /\bCS8785\b/,
    /\bbuild\s+(fail|broke|broken|error)\b/i,
    /\bfix\s+(this|the|my|a)?\s*(build|error|failure)\b/i,
    /\bwhy\s+(does|is|did)\s+(my\s+)?(build|compilation)\s+fail\b/i,
    /\brestore\s+fail/i,
    /\bpackage\s+not\s+found\b/i,
    /\bcannot\s+resolve\b/i,
    /\bmissing\s+(reference|assembly|package|SDK)\b/i,
    /\bsource\s+generator\b/i,
    /\banalyzer\s+(crash|error|exception|fail)\b/i,
  ],
  PERFORMANCE: [
    /\bslow\b/i,
    /\bperformance\b/i,
    /\boptimize\b/i,
    /\bspeed\s+up\b/i,
    /\bfaster\b/i,
    /\btoo\s+long\b/i,
    /\bbuild\s+time\b/i,
    /\bbottleneck\b/i,
    /\bincremental\b/i,
    /\bparallel\b/i,
    /\bcaching\b/i,
    /\bgraph\s+build\b/i,
    /\bbinlog\s+(analys|perf)/i,
    /\bwhy\s+(is|does)\s+(my\s+)?build\s+(so\s+)?slow\b/i,
  ],
  STYLE_REVIEW: [
    /\breview\b/i,
    /\bclean\s*up\b/i,
    /\banti[- ]?pattern\b/i,
    /\bbest\s+practice\b/i,
    /\bstyle\b/i,
    /\bimprove\b/i,
    /\brefactor\b/i,
    /\breadab(le|ility)\b/i,
    /\baudit\b/i,
    /\bcheck\s+(my\s+)?(csproj|project\s+file)\b/i,
    /\bidiomatic\b/i,
  ],
  MODERNIZATION: [
    /\bmodernize\b/i,
    /\bmigrat(e|ion)\b/i,
    /\blegacy\b/i,
    /\bold[- ]style\b/i,
    /\bSDK[- ]style\b/i,
    /\bupgrade\b/i,
    /\bconvert\b/i,
    /\bPackageReference\b/,
    /\bpackages\.config\b/i,
    /\bCentral\s+Package\b/i,
    /\bDirectory\.Build\b/i,
  ],
};

/**
 * Classify the intent of a user message.
 * Returns the intent key (e.g., 'BUILD_ERROR', 'PERFORMANCE').
 */
function classifyIntent(message) {
  if (!message || typeof message !== "string") return "GENERAL";

  // Score each intent by number of matching patterns
  const scores = {};
  for (const [intent, patterns] of Object.entries(INTENT_PATTERNS)) {
    scores[intent] = patterns.filter((p) => p.test(message)).length;
  }

  // Find the highest-scoring intent
  const best = Object.entries(scores).sort((a, b) => b[1] - a[1])[0];
  if (best && best[1] > 0) {
    return best[0];
  }

  return "GENERAL";
}

module.exports = { classifyIntent, INTENTS };
