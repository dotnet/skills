import { z } from "zod";

const assertionSchema = z.discriminatedUnion("type", [
  z.object({ type: z.literal("file_exists"), path: z.string().min(1) }),
  z.object({ type: z.literal("file_not_exists"), path: z.string().min(1) }),
  z.object({ type: z.literal("file_contains"), path: z.string().min(1), value: z.string().min(1) }),
  z.object({ type: z.literal("output_contains"), value: z.string().min(1) }),
  z.object({ type: z.literal("output_not_contains"), value: z.string().min(1) }),
  z.object({ type: z.literal("output_matches"), pattern: z.string().min(1) }),
  z.object({ type: z.literal("output_not_matches"), pattern: z.string().min(1) }),
  z.object({ type: z.literal("exit_success") }),
]);

const setupFileSchema = z.object({
  path: z.string(),
  source: z.string().optional(),
  content: z.string().optional(),
});

const setupSchema = z.object({
  files: z.array(setupFileSchema).optional(),
});

const scenarioSchema = z.object({
  name: z.string().min(1, "Scenario name is required"),
  prompt: z.string().min(1, "Scenario prompt is required"),
  setup: setupSchema.optional(),
  assertions: z.array(assertionSchema).optional(),
  rubric: z.array(z.string()).optional(),
  timeout: z.number().positive().optional().default(120),
  expect_tools: z.array(z.string()).optional(),
  reject_tools: z.array(z.string()).optional(),
  max_turns: z.number().positive().int().optional(),
  max_tokens: z.number().positive().int().optional(),
});

export const evalConfigSchema = z.object({
  scenarios: z.array(scenarioSchema).min(1, "At least one scenario is required"),
});

export type ParsedEvalConfig = z.infer<typeof evalConfigSchema>;

export function parseEvalConfig(data: unknown): ParsedEvalConfig {
  return evalConfigSchema.parse(data);
}

export function validateEvalConfig(
  data: unknown
): { success: true; data: ParsedEvalConfig } | { success: false; errors: string[] } {
  const result = evalConfigSchema.safeParse(data);
  if (result.success) {
    return { success: true, data: result.data };
  }
  return {
    success: false,
    errors: result.error.issues.map(
      (issue) => `${issue.path.join(".")}: ${issue.message}`
    ),
  };
}
