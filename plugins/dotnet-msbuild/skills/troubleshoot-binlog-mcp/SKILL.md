---
name: troubleshoot-binlog-mcp
description: "Diagnose dotnet-msbuild binlog MCP startup failures. USE FOR: CONNECTION_CLOSED, binlog server won't connect, missing binlog tools, Microsoft.AITools.BinlogMcp not found, dnx launch errors. Checks workspace SDK, feeds, and source mapping without bypassing policy. DO NOT USE FOR: errors inside a binlog (use binlog-failure-analysis), capturing binlogs (use binlog-generation), unrelated MCP servers."
license: MIT
---

# Troubleshoot Binlog MCP Startup

Explain why the plugin's binlog MCP server cannot connect and give an evidence-based,
policy-safe recovery. This diagnoses the **server launch**, not failures recorded in a build.
No binlog file or project build is needed.

This skill runs on demand. It cannot see a host error that was never exposed to the agent,
inject session-start context, or guarantee activation merely because a plugin was installed.

## 1. Establish what failed

Use the user's error and any host MCP status or startup logs already available.
`CONNECTION_CLOSED` alone says that the transport closed, not why. Do not translate it
directly into "package missing."

- If the binlog server is connected and its tools are discoverable, stop startup troubleshooting.
  Continue the original binlog analysis; do not change a working setup.
- If the plugin's skills loaded but its binlog server did not, continue below.
- If the plugin itself is not installed/enabled or no server is registered, address that host
  configuration first. Do not diagnose NuGet package resolution without a launch attempt.
- If execution or host logs are unavailable, explain that limit and give the next command for
  the user to run. Never claim to have inspected logs or repaired a connection.

## 2. Match the host's workspace and SDK

Discover the affected workspace directory and the host's actual MCP command, arguments, and
environment from available configuration. The shipped command is:

```text
dotnet dnx Microsoft.AITools.BinlogMcp --yes --prerelease
```

Run diagnostics from that workspace with the host's environment, not from the plugin directory
or a temporary directory with different NuGet settings. Inspect:

```text
dotnet --info
dotnet --version
dotnet nuget list source
```

Check the applicable `global.json` and inherited NuGet configuration, including parent/user
configuration, `clear`, disabled sources, and `packageSourceMapping`. A workspace `clear`
can replace the default nuget.org source with a private mirror that lacks this package.
An installed SDK is not necessarily the SDK selected in this directory. The shipped
`dotnet dnx` command requires a .NET 10+ SDK.

Do not dump credential settings, private feed URLs, tokens, or credential-provider output into
the answer. Inspect locally as needed and report a redacted source label and relevant error.

## 3. Capture the real launch error

Prefer an existing startup log from the failing host. Otherwise reproduce its **unchanged**
launch command once, from the affected workspace, capturing both stdout and stderr plus the
exit code. Do not initially add `--source`, replace the configuration, remove `global.json`,
clear caches, or install another copy: those change the conditions being diagnosed.

A stdio MCP server may remain running waiting for protocol input. Keep stdin open while
observing startup and use a bounded execution (for example, 30 seconds) with cleanup of only
the diagnostic process tree, including credential-provider children. Use the host's supported
timeout/cancellation facility; if it cannot safely clean up, ask the user to run the command
locally instead. Do not leave a background server running or kill unrelated `dotnet` processes.
A timeout, silence, or an open process is **not** proof that MCP initialized.

Only if package availability remains unclear, run this supporting probe under the same
workspace settings with a bounded execution:

```text
dotnet package search Microsoft.AITools.BinlogMcp --exact-match --prerelease --format json
```

Inspect the exact package ID and version in `searchResult[].packages`, and check both top-level
and per-source `problems`. Exit code zero alone does not establish a successful search.

| Observed evidence | Diagnosis and next action |
|-------------------|---------------------------|
| `dnx` reports no matching version in the configured feeds | Those settings did not resolve the package. Check whether the approved mirror provides prerelease versions of this exact package. |
| Search returns no packages and no source problems | Only the searched feeds lack a listed match. This is not proof that `dnx` must fail: a cached tool or unlisted version may still work. |
| Search has top-level/per-source problems, HTTP 401/403, TLS, network, or credential-provider errors | Availability is unverified, not "package missing." Address the specific feed/access failure first, even if search exited zero. |
| Search finds the package but `dnx` fails | Follow the launch error. Search does not apply package source mapping; check mapping, restore/download access, and runtime requirements. Do not declare the connection healthy. |
| Missing `dotnet`, unrecognized `dnx`, or SDK selection error | Fix the host's PATH/SDK selection. Account for `global.json`; do not silently upgrade the repository or remove its SDK pin. |
| Timeout, unsupported search command, or malformed/non-JSON output | Report an inconclusive probe. Use launch logs or a user-run diagnostic; do not infer absence or success. |
| Launch succeeds but host connection still fails | Investigate the host's actual command/environment and MCP initialization logs; package discovery is no longer the demonstrated failure. |

Do not repeat expensive probes once the evidence establishes the next action.

## 4. Choose a policy-safe recovery

Recommend the smallest change supported by the observed failure:

- Have the feed owner mirror `Microsoft.AITools.BinlogMcp` and its required versions/dependencies
  into an approved feed, or correct an approved mapping that excludes it.
- Repair the approved feed's authentication, connectivity, or certificate configuration when
  that is the observed problem. Never suggest disabling certificate validation.
- Add nuget.org only if organizational policy permits and the user approves the change.
  Do not remove `clear`, disable source mapping, or override sources to bypass enterprise policy.
- If appropriate and approved, install the tool from an approved feed and register its
  `binlog-mcp` executable with the host as an alternative MCP server. Confirm the executable's
  path and avoid registering a duplicate server. Installation alone is not connection recovery.
- For SDK/PATH issues, make the required SDK available to the host or use a deliberately
  configured alternative launch. Ask before changing repository SDK selection.

Do not edit NuGet settings, host registration, or SDK pins without the user's approval.
If policy prevents recovery, say so. For an existing binlog, offer the text-log replay fallback
in `binlog-failure-analysis` rather than blocking build diagnosis on MCP repair.

## 5. Verify and report

After an approved change, reconnect/restart the server using the host's supported controls.
Confirm MCP initialization and a tool listing that contains binlog tools such as
`binlog_overview`. If a binlog is available, resume the original analysis.

Report the observed cause (or what remains unknown), the relevant redacted evidence, the
smallest recovery, and what was actually verified. End with one status line:

```text
MCP status: connected
```

Use `connected` only after the host discovers the server's tools, `failed` for an observed
launch/connection failure, and `unverified` when only probes or advice are available.
Never substitute "package found" or "tool installed" for a verified MCP connection.
