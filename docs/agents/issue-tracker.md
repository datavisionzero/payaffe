# Issue tracker: GitHub

Issues for this repo live as GitHub issues. Use the `gh` CLI for all operations.

## Conventions

- **Create an issue**: `gh issue create --title "..." --body "..."`. Use a heredoc for multi-line bodies.
- **Read an issue**: `gh issue view <number> --comments`.
- **List issues**: `gh issue list --state open --json number,title,body,labels --jq '[.[] | {number, title, labels: [.labels[].name]}]'` with appropriate `--label` and `--state` filters.
- **Comment on an issue**: `gh issue comment <number> --body "..."`
- **Apply / remove labels**: `gh issue edit <number> --add-label "..."` / `--remove-label "..."`
- **Close**: `gh issue close <number> --comment "..."`

Infer the repo from `git remote -v` — `gh` does this automatically when run inside a clone.

## What belongs in an issue, and what does not

An issue tracks work. It is not where decisions are recorded: an accepted
decision becomes an ADR under [../adr/](../adr/), and the issue closes pointing
at it. A rule the implementation must follow belongs in the matching baseline
document under [../architecture/](../architecture/), not in a comment thread.

Security reports do not belong in issues at all. They go through GitHub's
private vulnerability reporting; see [../../SECURITY.md](../../SECURITY.md).

## Pull requests as a triage surface

**PRs as a request surface: no.**

GitHub shares one number space across issues and PRs, so a bare `#42` may be
either — resolve with `gh pr view 42` and fall back to `gh issue view 42`.
