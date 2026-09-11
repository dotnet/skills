# Vendor readiness quickstart (local preview)

Use `dotnet-blazor` to request evidence-linked readiness reports for a released Blazor
package or a named control. This guide describes the local preview workflow.
The observations below concern an earlier **0.1.2** candidate, not acceptance of
this contribution's changed bytes or its newer optional package-preparation command.

> [!IMPORTANT]
> **Report delivery is not executable coverage.** One ordinary package request and
> one independent Button request delivered substantive reports without operator
> report repair. Button's combined consumer-build/browser-install call and its
> subsequent build-only call were denied by CLI host permissions:
> `Permission denied and could not request permission from user`. The narrower
> cause was not established. Its source/package/document report explicitly leaves
> runtime, browser, trimming, and AOT work **not tested**; it does not establish
> runtime accessibility coverage either. This demonstrates bounded report delivery,
> not unrestricted vendor-host parity, universal self-service, or certification.

Both requests used Copilot CLI **1.0.84-4 on Windows** and **`gpt-6-astra`**.
The package request took about 17m15s / 43 model calls; Button took about
18m26s / 45 model calls. These are observations, not time, usage, or cost promises.
No other vendor, model, or host was exercised.

## Prerequisites

Use PowerShell and an authorized Copilot CLI account. The bundled BCL-only
`net11.0` validator requires an **active .NET 11 SDK**; the observed SDK was
`11.0.100-rc.1.26425.128`. Consumer evidence has a different role: use a stable SDK
supported by the vendor package (`10.0.401` in these journeys). That consumer SDK
does not replace the validator's .NET 11 prerequisite.

Keep the preview checkout, CLI profile, assessment workspace, and build scratch
separate. Provide writable scratch outside the inputs and plugin through
`READINESS_TEMP`. Do not install machine-level tools, change global feeds or real
user configuration, or copy credentials as an assessment workaround. Stop for
ordinary authentication or prerequisite failures rather than bypassing them.

## Install the local preview

Choose directories you own and create a neutral workspace and fresh, empty profile
outside the assessment inputs. Replace these example Windows paths with your own;
the marketplace path must contain the preview checkout or snapshot and its manifests.

```powershell
$env:COPILOT_HOME = 'C:\Readiness\package\copilot-home'
Set-Location -LiteralPath 'C:\Readiness\package\workspace'
copilot --no-auto-update plugin marketplace add 'C:\Readiness\preview-marketplace'
copilot --no-auto-update plugin install dotnet-blazor@dotnet-agent-skills
copilot --no-auto-update plugin list --json
```

Each observed journey used these commands independently with its own profile and
workspace. The CLI reported `source=live` and "nothing was copied": **keep the
preview checkout in place and unchanged**. The listing showed one enabled plugin,
`dotnet-blazor`, and 14 disabled catalog entries. This establishes local live
registration, not copied-payload or remote installation.

If you installed the earlier `dotnet-blazor-component-readiness` preview, remove
it with your client's plugin manager before enabling `dotnet-blazor`; keeping both
can register duplicate readiness skill and agent identities.

## Supply inputs and make an ordinary request

Place the exact `.nupkg`, corresponding public source, available release documents,
and provenance under `.\inputs`. Record their origins, package version, and source
revision. Preserve original bytes. Vendor instruction and skill files belong under
`inputs\source` as assessed data, not in host instruction or skill configuration.

Start each independent journey with its own inputs directory and no prior report,
private recovery kit, or generated expected answers. Use a separate workspace,
profile, and scratch directory for the named-control journey. Setting
`COPILOT_HOME` alone does not prove isolation: review active plugin/instruction
discovery and keep unrelated readiness plugins and prior conversation memory out.

Synthetic package-only example:

```text
Create a readiness report for the Sample.Controls 0.1.2-alpha.3 package. Use the inputs in .\inputs and write the report under .\reports.
```

Synthetic named-control example:

```text
Create a readiness report for the Button (SampleButton) control in Sample.Controls 0.1.2-alpha.3. Use the inputs in .\inputs and write the report under .\reports.
```

Substitute your package, exact version, and control name; these examples are
usage guidance, not evidence that the synthetic subjects were assessed. Let the installed
skill handle the ordinary request. A fresh named-control request can include shared
package context without a prewritten package report. Common applicable owner evidence
can be reused, but component verdicts must not be copied from sibling controls.

## Review permissions deliberately

The accepted runs were noninteractive with fixed controls. As **untested interactive
guidance**, start an ordinary CLI session in the neutral workspace with
`--no-custom-instructions` and review individual approvals. Do not treat nested vendor
instructions as host instructions or grant the full preview repository merely to read
the installed plugin.

Grant only the needed input/plugin reads, report and separate scratch writes, public
evidence access, and build/browser tools. Do not use blanket path/URL grants or bypass
denied operations. If a required operation is denied, retain the limited result and
review permissions before a separately authorized attempt, not an automatic retry
loop. Do not manually run validator commands or edit generated artifacts to repair
the report. Assessment source inputs must remain unchanged.

## Read and share the result

Both journeys delivered these human-facing outputs:

```text
reports\readable\report.md
reports\readable\evidence.md
reports\readable\mapping.json
reports\readable\reader.validation.json
```

Canonical artifacts were under `reports\revisions\0001`: `package.*` files for the
package request, and `unified.*` files for Button plus shared package context.
These are examples of the delivered paths.

Keep the evidence companions, mapping, validation records, and linked technical
artifacts with the report; review sharing permissions before distributing them.
Missing owner-held records, unperformed work, and non-applicability are distinct
outcomes, not invented failures. Review supported findings and limitations:
report completion means the requested assessment is accounted for, not that every
check passed, all executable work ran, or release approval or certification was granted.
