export interface SkillInfo {
  name: string;
  description: string;
  path: string;
  skillMdPath: string;
  skillMdContent: string;
  evalPath: string | null;
  evalConfig: EvalConfig | null;
}

export interface EvalConfig {
  scenarios: EvalScenario[];
}

export interface EvalScenario {
  name: string;
  prompt: string;
  setup?: SetupConfig;
  assertions?: Assertion[];
  rubric?: string[];
  timeout?: number;
  expect_tools?: string[];
  reject_tools?: string[];
  max_turns?: number;
  max_tokens?: number;
}

export interface SetupConfig {
  files?: SetupFile[];
}

export interface SetupFile {
  path: string;
  source?: string;
  content?: string;
}

export type AssertionType =
  | "file_exists"
  | "file_not_exists"
  | "file_contains"
  | "output_contains"
  | "output_not_contains"
  | "output_matches"
  | "output_not_matches"
  | "exit_success"
  | "expect_tools"
  | "reject_tools"
  | "max_turns"
  | "max_tokens";

export interface Assertion {
  type: AssertionType;
  path?: string;
  value?: string;
  pattern?: string;
}

export interface AssertionResult {
  assertion: Assertion;
  passed: boolean;
  message: string;
}

export interface RunMetrics {
  tokenEstimate: number;
  toolCallCount: number;
  toolCallBreakdown: Record<string, number>;
  turnCount: number;
  wallTimeMs: number;
  errorCount: number;
  assertionResults: AssertionResult[];
  taskCompleted: boolean;
  agentOutput: string;
  events: AgentEvent[];
  workDir: string;
}

export interface AgentEvent {
  type: string;
  timestamp: number;
  data: Record<string, unknown>;
}

export interface JudgeResult {
  rubricScores: RubricScore[];
  overallScore: number;
  overallReasoning: string;
}

export interface RubricScore {
  criterion: string;
  score: number;
  reasoning: string;
}

export interface RunResult {
  metrics: RunMetrics;
  judgeResult: JudgeResult;
}

// --- Pairwise judging types ---

export type PairwiseMagnitude =
  | "much-better"
  | "slightly-better"
  | "equal"
  | "slightly-worse"
  | "much-worse";

export interface PairwiseRubricResult {
  criterion: string;
  winner: "baseline" | "skill" | "tie";
  magnitude: PairwiseMagnitude;
  reasoning: string;
}

export interface PairwiseJudgeResult {
  rubricResults: PairwiseRubricResult[];
  overallWinner: "baseline" | "skill" | "tie";
  overallMagnitude: PairwiseMagnitude;
  overallReasoning: string;
  positionSwapConsistent: boolean;
}

export const PAIRWISE_MAGNITUDE_SCORES: Record<PairwiseMagnitude, number> = {
  "much-better": 1.0,
  "slightly-better": 0.4,
  "equal": 0.0,
  "slightly-worse": -0.4,
  "much-worse": -1.0,
};

export type JudgeMode = "pairwise" | "independent" | "both";

export interface ScenarioComparison {
  scenarioName: string;
  baseline: RunResult;
  withSkill: RunResult;
  improvementScore: number;
  breakdown: MetricBreakdown;
  pairwiseResult?: PairwiseJudgeResult;
  perRunScores?: number[];
}

export interface MetricBreakdown {
  tokenReduction: number;
  toolCallReduction: number;
  taskCompletionImprovement: number;
  timeReduction: number;
  qualityImprovement: number;
  overallJudgmentImprovement: number;
  errorReduction: number;
}

export interface ConfidenceInterval {
  low: number;
  high: number;
  level: number;
}

export interface SkillVerdict {
  skillName: string;
  skillPath: string;
  passed: boolean;
  scenarios: ScenarioComparison[];
  overallImprovementScore: number;
  normalizedGain?: number;
  confidenceInterval?: ConfidenceInterval;
  isSignificant?: boolean;
  reason: string;
  profileWarnings?: string[];
}

export interface ValidatorConfig {
  minImprovement: number;
  requireCompletion: boolean;
  requireEvals: boolean;
  strict: boolean;
  verbose: boolean;
  model: string;
  judgeModel: string;
  judgeMode: JudgeMode;
  runs: number;
  judgeTimeout: number;
  confidenceLevel: number;
  reporters: ReporterSpec[];
  skillPaths: string[];
  saveResults: boolean;
  resultsDir: string;
  testsDir?: string;
}

export interface ReporterSpec {
  type: "console" | "json" | "junit";
  outputPath?: string;
}

export const DEFAULT_WEIGHTS: Record<keyof MetricBreakdown, number> = {
  tokenReduction: 0.05,
  toolCallReduction: 0.025,
  taskCompletionImprovement: 0.15,
  timeReduction: 0.025,
  qualityImprovement: 0.40,
  overallJudgmentImprovement: 0.30,
  errorReduction: 0.05,
};
