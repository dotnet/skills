import assert from "node:assert/strict";
import { spawn, spawnSync } from "node:child_process";
import { createInterface } from "node:readline";
import { mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { isAbsolute, join, resolve } from "node:path";

const supportedCodexVersion = "0.154.0";
const marketplaceName = "dotnet-agent-skills";
const pluginName = "dotnet-msbuild";
const expectedSkill = `${pluginName}:binlog-failure-analysis`;
const expectedMcpServer = "binlog";
const expectedMcpTool = "binlog_overview";

const options = parseArguments(process.argv.slice(2));
const repositoryRoot = resolve(options.repository ?? process.cwd());
const codex = options.codex ?? process.env.CODEX_BIN ?? "codex";
const stateRoot = resolve(
  process.env.CODEX_SMOKE_HOME ??
    join(repositoryRoot, "artifacts", "codex-plugin-smoke"),
);
const codexHome = join(stateRoot, "codex-home");
const dotnetHome = join(stateRoot, "dotnet-home");
const nugetPackages = join(stateRoot, "nuget-packages");
const environment = {
  ...process.env,
  CODEX_HOME: codexHome,
  DOTNET_CLI_HOME: dotnetHome,
  DOTNET_ADD_GLOBAL_TOOLS_TO_PATH: "false",
  DOTNET_CLI_TELEMETRY_OPTOUT: "1",
  DOTNET_GENERATE_ASPNET_CERTIFICATE: "false",
  DOTNET_NOLOGO: "1",
  DOTNET_SKIP_FIRST_TIME_EXPERIENCE: "1",
  NUGET_PACKAGES: nugetPackages,
};
delete environment.CODEX_API_KEY;
delete environment.OPENAI_API_KEY;

try {
  rmSync(stateRoot, { recursive: true, force: true });
  mkdirSync(codexHome, { recursive: true });
  mkdirSync(dotnetHome, { recursive: true });
  mkdirSync(nugetPackages, { recursive: true });

  const version = runCodex(["--version"]).trim();
  assert.match(
    version,
    new RegExp(`\\b${supportedCodexVersion.replaceAll(".", "\\.")}$`),
    `Expected Codex ${supportedCodexVersion}, got: ${version}`,
  );

  const marketplace = runCodexJson([
    "plugin",
    "marketplace",
    "add",
    repositoryRoot,
    "--json",
  ]);
  assert.equal(marketplace.marketplaceName, marketplaceName);

  const marketplaceManifest = JSON.parse(
    readFileSync(
      join(repositoryRoot, ".agents", "plugins", "marketplace.json"),
      "utf8",
    ),
  );
  const expectedPlugins = marketplaceManifest.plugins.map((plugin) => plugin.name);
  for (const name of expectedPlugins) {
    const installed = runCodexJson([
      "plugin",
      "add",
      `${name}@${marketplaceName}`,
      "--json",
    ]);
    assert.equal(installed.name, name);
    assert.equal(installed.marketplaceName, marketplaceName);
  }

  const plugins = runCodexJson([
    "plugin",
    "list",
    "--marketplace",
    marketplaceName,
    "--json",
  ]);
  const installedPlugins = new Set(
    plugins.installed
      .filter((plugin) => plugin.enabled)
      .map((plugin) => plugin.name),
  );
  assert.deepEqual(installedPlugins, new Set(expectedPlugins));

  const mcpServers = runCodexJson(["mcp", "list", "--json"]);
  const binlog = mcpServers.find(
    (server) => server.name === expectedMcpServer && server.enabled,
  );
  assert.ok(binlog, "Codex did not discover the enabled binlog MCP server");
  assert.equal(binlog.transport.type, "stdio");

  runCommand("dotnet", [
    "tool",
    "install",
    "Microsoft.AITools.BinlogMcp",
    "--tool-path",
    join(stateRoot, "binlog-tool"),
    "--prerelease",
  ]);

  const sampleRoot = join(stateRoot, "sample");
  const sampleBinlog = join(stateRoot, "sample.binlog");
  mkdirSync(sampleRoot, { recursive: true });
  writeFileSync(
    join(sampleRoot, "sample.csproj"),
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net11.0</TargetFramework></PropertyGroup></Project>',
  );
  writeFileSync(
    join(sampleRoot, "Program.cs"),
    'System.Console.WriteLine("smoke");',
  );
  runCommand("dotnet", [
    "build",
    join(sampleRoot, "sample.csproj"),
    `-bl:${sampleBinlog}`,
    "--nologo",
  ]);

  await testAppServer(sampleBinlog);

  console.log(
    `Codex ${supportedCodexVersion} installed ${expectedPlugins.length} plugins, discovered ${expectedSkill} and ${expectedMcpServer}, and called ${expectedMcpTool}.`,
  );
} finally {
  if (process.env.CODEX_SMOKE_KEEP_HOME !== "1") {
    await removeStateRoot();
  }
}

async function testAppServer(sampleBinlog) {
  const appServer = spawn(codex, ["app-server", "--stdio"], {
    cwd: repositoryRoot,
    env: environment,
    stdio: ["pipe", "pipe", "pipe"],
  });
  const pending = new Map();
  const stderr = [];
  let nextId = 1;

  createInterface({ input: appServer.stdout }).on("line", (line) => {
    let message;
    try {
      message = JSON.parse(line);
    } catch {
      return;
    }

    if (message.id === undefined) {
      return;
    }

    const request = pending.get(message.id);
    if (!request) {
      return;
    }

    pending.delete(message.id);
    clearTimeout(request.timeout);
    if (message.error) {
      request.reject(
        new Error(
          `${request.method} failed: ${JSON.stringify(message.error)}`,
        ),
      );
    } else {
      request.resolve(message.result);
    }
  });

  createInterface({ input: appServer.stderr }).on("line", (line) => {
    stderr.push(line);
    console.error(`[codex app-server] ${line}`);
  });

  const exit = new Promise((resolveExit) => {
    appServer.once("exit", (code, signal) => {
      const error = new Error(
        `Codex app-server exited before the smoke test completed (code ${code}, signal ${signal}).\n${stderr.join("\n")}`,
      );
      for (const request of pending.values()) {
        clearTimeout(request.timeout);
        request.reject(error);
      }
      pending.clear();
      resolveExit();
    });
  });

  function notify(method, params) {
    const message = params === undefined ? { method } : { method, params };
    appServer.stdin.write(`${JSON.stringify(message)}\n`);
  }

  function request(method, params, timeoutMs = 180_000) {
    const id = nextId++;
    return new Promise((resolveRequest, rejectRequest) => {
      const timeout = setTimeout(() => {
        pending.delete(id);
        rejectRequest(
          new Error(
            `${method} timed out after ${timeoutMs} ms.\n${stderr.join("\n")}`,
          ),
        );
      }, timeoutMs);
      pending.set(id, {
        method,
        resolve: resolveRequest,
        reject: rejectRequest,
        timeout,
      });
      appServer.stdin.write(`${JSON.stringify({ id, method, params })}\n`);
    });
  }

  try {
    await request("initialize", {
      clientInfo: {
        name: "dotnet-skills-codex-smoke",
        version: "1.0.0",
      },
      capabilities: {
        experimentalApi: true,
      },
    });
    notify("initialized");

    const skills = await request("skills/list", {
      cwds: [repositoryRoot],
      forceReload: true,
    });
    const discoveredSkills = skills.data.flatMap((entry) => entry.skills);
    assert.ok(
      discoveredSkills.some(
        (skill) =>
          skill.name === expectedSkill &&
          skill.pluginId === `${pluginName}@${marketplaceName}`,
      ),
      `Codex did not discover ${expectedSkill}`,
    );

    const thread = await request("thread/start", {
      cwd: repositoryRoot,
      ephemeral: true,
      model: "qwen3-coder",
      modelProvider: "ollama",
    });
    const threadId = thread.thread.id;

    const result = await request("mcpServer/tool/call", {
      threadId,
      server: expectedMcpServer,
      tool: expectedMcpTool,
      arguments: {
        binlog_file: sampleBinlog,
      },
    });
    assert.notEqual(result.isError, true, `${expectedMcpTool} returned an error`);
    assert.ok(
      (result.content?.length ?? 0) > 0 || result.structuredContent,
      `${expectedMcpTool} returned no build overview`,
    );
  } finally {
    appServer.stdin.end();
    const exitedGracefully = await Promise.race([
      exit.then(() => true),
      new Promise((resolveTimeout) => {
        setTimeout(() => resolveTimeout(false), 15_000);
      }),
    ]);
    if (!exitedGracefully) {
      appServer.kill();
      await exit;
    }
  }
}

function runCodex(args) {
  return runCommand(codex, args);
}

function runCommand(command, args) {
  const result = spawnSync(command, args, {
    cwd: repositoryRoot,
    env: environment,
    encoding: "utf8",
    timeout: 180_000,
  });
  assert.equal(
    result.status,
    0,
    `${command} ${args.join(" ")} failed.\nstdout:\n${result.stdout}\nstderr:\n${result.stderr}`,
  );
  return result.stdout;
}

function runCodexJson(args) {
  return JSON.parse(runCodex(args));
}

function parseArguments(args) {
  const parsed = {};
  for (let index = 0; index < args.length; index += 1) {
    const argument = args[index];
    if (argument === "--codex") {
      parsed.codex = resolve(args[++index]);
    } else if (argument === "--repository") {
      parsed.repository = resolve(args[++index]);
    } else {
      throw new Error(`Unknown argument: ${argument}`);
    }
  }

  if (parsed.codex && !isAbsolute(parsed.codex)) {
    throw new Error("--codex must be an absolute path");
  }
  return parsed;
}

async function removeStateRoot() {
  for (let attempt = 1; attempt <= 20; attempt += 1) {
    try {
      rmSync(stateRoot, { recursive: true, force: true });
      return;
    } catch (error) {
      if (attempt === 20) {
        throw error;
      }
      await new Promise((resolveTimeout) => setTimeout(resolveTimeout, 1_000));
    }
  }
}
