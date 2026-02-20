import { mkdtemp, cp, writeFile, mkdir } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join, dirname, resolve, sep } from "node:path";
import type {
  CopilotClient,
  CopilotClientOptions,
  SessionConfig,
  SessionEvent,
  PermissionRequest,
  PermissionRequestResult,
} from "@github/copilot-sdk";
import type {
  EvalScenario,
  RunMetrics,
  AgentEvent,
  SkillInfo,
} from "./types.js";
import { collectMetrics } from "./metrics.js";

export interface RunOptions {
  scenario: EvalScenario;
  skill: SkillInfo | null;
  model: string;
  verbose: boolean;
  log?: (message: string) => void;
  client?: CopilotClient;
}

async function setupWorkDir(
  scenario: EvalScenario,
  skillPath: string | null
): Promise<string> {
  const workDir = await mkdtemp(join(tmpdir(), "skill-validator-"));

  if (scenario.setup?.files) {
    for (const file of scenario.setup.files) {
      const targetPath = join(workDir, file.path);
      await mkdir(join(targetPath, ".."), { recursive: true });

      if (file.content) {
        await writeFile(targetPath, file.content, "utf-8");
      } else if (file.source && skillPath) {
        const sourcePath = join(skillPath, file.source);
        await cp(sourcePath, targetPath);
      }
    }
  }

  return workDir;
}

let _sharedClient: CopilotClient | null = null;

export async function getSharedClient(verbose: boolean): Promise<CopilotClient> {
  if (_sharedClient) return _sharedClient;
  const mod = await import("@github/copilot-sdk");
  const options: CopilotClientOptions = {
    logLevel: verbose ? "info" : "none",
    ...(process.env.GITHUB_TOKEN ? { githubToken: process.env.GITHUB_TOKEN } : {}),
  };
  _sharedClient = new mod.CopilotClient(options);
  await _sharedClient.start();
  return _sharedClient;
}

export async function stopSharedClient(): Promise<void> {
  if (_sharedClient) {
    await _sharedClient.stop();
    _sharedClient = null;
  }
}

export function checkPermission(
  req: PermissionRequest,
  workDir: string,
  skillPath?: string
): PermissionRequestResult {
  const reqPath = String(req.path ?? req.command ?? "");
  if (!reqPath) return { kind: "approved" };

  const resolved = resolve(reqPath);
  const allowedDirs = [resolve(workDir)];
  if (skillPath) allowedDirs.push(resolve(skillPath));

  if (allowedDirs.some((dir) => resolved === dir || resolved.startsWith(dir + sep))) {
    return { kind: "approved" };
  }

  return { kind: "denied-by-rules" };
}

export function buildSessionConfig(
  skill: SkillInfo | null,
  model: string,
  workDir: string
): SessionConfig {
  const skillPath = skill ? dirname(skill.path) : undefined;
  return {
    model,
    streaming: true,
    workingDirectory: workDir,
    skillDirectories: skill ? [skillPath!] : [],
    infiniteSessions: { enabled: false },
    onPermissionRequest: async (req: PermissionRequest) => {
      return checkPermission(req, workDir, skillPath);
    },
  };
}

export async function runAgent(options: RunOptions): Promise<RunMetrics> {
  const { scenario, skill, model, verbose } = options;
  const workDir = await setupWorkDir(scenario, skill?.path ?? null);
  const events: AgentEvent[] = [];
  let agentOutput = "";

  const startTime = Date.now();

  try {
    const client = await getSharedClient(verbose);

    const session = await client.createSession(
      buildSessionConfig(skill, model, workDir)
    );

    const idlePromise = new Promise<void>((resolve, reject) => {
      const timer = setTimeout(() => {
        reject(new Error(`Scenario timed out after ${scenario.timeout}s`));
      }, (scenario.timeout ?? 120) * 1000);

      session.on((event: SessionEvent) => {
        const agentEvent: AgentEvent = {
          type: event.type,
          timestamp: Date.now(),
          data: event.data as Record<string, unknown>,
        };
        events.push(agentEvent);

        if (
          event.type === "assistant.message_delta" &&
          typeof event.data.deltaContent === "string"
        ) {
          agentOutput += event.data.deltaContent;
        }

        if (
          event.type === "assistant.message" &&
          typeof event.data.content === "string" &&
          event.data.content !== ""
        ) {
          agentOutput = event.data.content;
        }

        if (verbose) {
          const write = options.log ?? ((msg: string) => process.stderr.write(`${msg}\n`));
          if (event.type === "tool.execution_start") {
            write(`      🔧 ${event.data.toolName}`);
          } else if (event.type === "assistant.message") {
            write(`      💬 Response received`);
          }
        }

        if (event.type === "session.idle") {
          clearTimeout(timer);
          resolve();
        }

        if (event.type === "session.error") {
          clearTimeout(timer);
          reject(new Error(String(event.data.message || "Session error")));
        }
      });
    });

    await session.send({ prompt: scenario.prompt });
    await idlePromise;

    await session.destroy();
  } catch (error) {
    events.push({
      type: "runner.error",
      timestamp: Date.now(),
      data: { message: String(error) },
    });
  }

  const wallTimeMs = Date.now() - startTime;

  return collectMetrics(events, agentOutput, wallTimeMs, workDir);
}
