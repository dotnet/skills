# MSBuild Agentic Workflow Templates

These are [GitHub Agentic Workflow](https://github.com/github/gh-aw) templates for MSBuild and .NET build automation.

## Setup

1. Install the `gh aw` CLI extension
2. Copy the desired workflow files to your repository's `.github/workflows/` directory
3. Copy the `shared/` directory as well (workflows import from it)
4. Compile: `gh aw compile`
5. Commit both the `.md` and generated `.lock.yml` files
6. The workflows will now run automatically based on their triggers

## Customization

- Edit the `on:` section to match your CI workflow names
- Adjust `safe-outputs` limits as needed
