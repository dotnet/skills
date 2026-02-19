# Copilot Skills Evaluation

Automated pipeline for measuring whether a component's skill improves Copilot's responses to build-related problems.

## How It Works

Each test is run **twice** through Copilot CLI:

| Run | Components | Purpose |
|-----|---------|---------|
| **Vanilla** | None | Baseline — what Copilot produces on its own |
| **Skilled** | Component installed | What Copilot produces with the skills component |

Both outputs are then scored by a separate Copilot invocation (acting as an evaluator) against an expected-output rubric, producing per-test quality scores (Accuracy, Completeness, Actionability, Clarity — each 0–10) along with token and time metrics.

## Test Structure

```
src/<component>/tests/
├── <test-name>/
│   ├── expected-output.md        # Grading rubric (required for automated eval)
│   ├── eval-test-prompt.txt      # Custom prompt override (optional)
│   ├── README.md                 # Human documentation (excluded from eval)
│   └── <project files>           # Test project files (copied to temp dir)
└── ...
```

Results are written to `artifacts/TestResults/`.

### File Conventions

| File | Purpose | Copied to eval temp dir? |
|------|---------|--------------------------|
| `expected-output.md` | Grading rubric for evaluator | ❌ No — read directly |
| `eval-test-prompt.txt` | Custom prompt (overrides default) | ❌ No — read from source dir |
| `README.md` | Human documentation | ❌ No — excluded from eval copy |
| `.gitignore` | Git ignore rules | ❌ No — excluded from eval copy |
| Everything else | Test project files | ✅ Yes — copied to temp dir |

### Adding a New Test

1. Create a test in the component's `tests/<name>/` directory with project files that exhibit the build problem.
2. Add `expected-output.md` describing the expected diagnosis, key concepts, and fixes.
3. Optionally add `eval-test-prompt.txt` if the default prompt ("Analyze the build issues...") doesn't fit.
4. Ensure no hint-comments (e.g., `<!-- BAD: ... -->`, `// CS0246: ...`) remain in project files.
5. The pipeline will auto-discover any test folder containing `expected-output.md`.

> **Note:** Only project files are copied to a temp directory for each run. `README.md`, `expected-output.md`, `eval-test-prompt.txt`, and `.gitignore` are excluded from the temp copy to avoid leaking answers to the AI.

## Pipeline Steps

1. **Discover tests** — finds all `src/<component>/tests/*/expected-output.md` directories.
2. **Vanilla run** — uninstalls the skills component, runs each test through Copilot CLI.
3. **Skilled run** — installs the skills component, runs each test again.
4. **Evaluate** — uninstalls the component, then uses Copilot CLI (as a neutral evaluator) to score both outputs against `expected-output.md`.
5. **Generate summary** — aggregates scores and stats into a markdown table.

## Running Locally

### Prerequisites

| Tool | Purpose |
|------|---------|
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | Container runtime for `act` |
| [act](https://github.com/nektos/act) | Runs GitHub Actions workflows locally |
| [GitHub Copilot CLI](https://docs.github.com/en/copilot/how-tos/set-up/install-copilot-cli) (`npm i -g @github/copilot`) | Pre-installed in the Docker image |

### One-Time Setup

1. **Build the Docker image** with pwsh, dotnet, and copilot pre-installed (avoids repeated installs):

   ```powershell
   # Start from the base act image
   docker run --name act-pwsh-build -d catthehacker/ubuntu:act-latest tail -f /dev/null

   # Install pwsh, dotnet SDK, and copilot CLI inside the container
   docker exec act-pwsh-build bash -c '
     apt-get update && apt-get install -y wget apt-transport-https &&
     . /etc/os-release &&
     wget -q "https://packages.microsoft.com/config/ubuntu/$VERSION_ID/packages-microsoft-prod.deb" &&
     dpkg -i packages-microsoft-prod.deb && rm packages-microsoft-prod.deb &&
     apt-get update && apt-get install -y powershell dotnet-sdk-8.0 &&
     export PATH=/opt/acttoolcache/node/24.13.0/x64/bin:$PATH &&
     npm install -g @github/copilot &&
     ln -sf /opt/acttoolcache/node/24.13.0/x64/bin/copilot /usr/local/bin/copilot
   '

   # Commit the container as a reusable image
   docker commit act-pwsh-build act-pwsh:latest
   docker stop act-pwsh-build && docker rm act-pwsh-build
   ```

2. **Create a `.secrets` file** in the repo root with your GitHub token:

   ```
   COPILOT_GITHUB_TOKEN=ghp_your_token_here
   ```

### Run the Pipeline

From the repository root, in **PowerShell**:

```powershell
$ErrorActionPreference = "Continue"
& act workflow_dispatch `
    --pull=false `
    -P ubuntu-latest=act-pwsh:latest `
    --use-new-action-cache `
    --secret-file .secrets `
    --bind `
    --artifact-server-path "$PWD/.act-artifacts" `
    --env "GITHUB_RUN_ID=$(Get-Date -Format 'yyyyMMdd-HHmmss')" `
    2>&1 | Tee-Object -FilePath act-eval.log
```

Results will appear in `artifacts/TestResults/<component>/<test>/<timestamp>/` (e.g., `20260218-120000`).
Uploaded artifacts are stored in `.act-artifacts/` (git-ignored).

> **⚠️ Windows + act caveat:** You must invoke `act` directly from PowerShell with `2>&1 | Tee-Object` (or `2>&1 | Out-Host`). Using `cmd /c act ... > file 2>&1` causes `context canceled` errors that kill long-running Docker exec steps. This is a known issue with act v0.2.x on Windows.

### Understanding the Output

The summary table shows per-test comparisons:

| Column | Meaning |
|--------|---------|
| **Quality** | Score delta (e.g. `++ 1` means skilled scored 1 point higher) |
| **Time** | Wall-clock time delta (negative = skilled was faster) |
| **Tokens (in)** | Input token delta (negative = skilled used fewer tokens) |
| **Winner** | Which run produced the better result |

If the `Upload Artifacts` step fails with `Unable to get the ACTIONS_RUNTIME_TOKEN env variable`, ensure you are passing `--artifact-server-path` to act (see the command above). This flag starts a local artifact server; without it, the required runtime variables are not set.
