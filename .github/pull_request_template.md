## Summary

<!-- Describe the change and why it is needed. Keep the scope focused. -->

## Related issue

<!-- Link the issue this addresses, for example: Fixes #123. Use N/A for small fixes. -->

## Validation

<!-- List the commands you ran and their results, or explain why validation is not applicable. -->

## Checklist

- [ ] I searched existing issues and pull requests to avoid duplicates.
- [ ] I kept this pull request focused and avoided unrelated refactors.
- [ ] I added or updated tests, evals, or documentation when changing skill or agent behavior.
- [ ] I updated CODEOWNERS when adding or moving owned content.
- [ ] I updated all marketplace manifests when plugin metadata changed.
- [ ] I updated `eng/known-domains.txt` for any new external domains referenced by skill content.

<details>
<summary>Evaluation changes only</summary>

For every eval-related change, including a new eval:

- [ ] Scenarios are necessary, distinct, and use natural prompts.
- [ ] Graders cover the full result, accept the golden result, and reject a realistic mutation.
- [ ] No-op, dormancy, and statistical power are covered where needed.
- [ ] I ran the applicable production evaluation path and recorded the result above.
- [ ] If this fixes a failed eval, I classified the failure before editing skill content.
- [ ] If this broadly changes routing or behavior, I checked separate model-family evidence.

</details>
