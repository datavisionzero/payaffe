# Public issue tracking: GitHub

GitHub Issues is the public request and discussion surface for this repository.
It accepts bug reports, feature requests, and other contributions from outside
the core development workflow. It is not necessarily a complete development
backlog.

Use the `gh` CLI for GitHub issue operations.

## Publication boundary

Everything in a GitHub issue is public. Create or update one only when:

- the work already originates from a public GitHub issue;
- a maintainer explicitly asks for a public issue; or
- the information is intentionally being published for external discussion.

Do not publish private planning notes, credentials, security-sensitive details,
or non-public follow-up work as GitHub issues. When such follow-up work emerges,
describe it in the handoff so a maintainer can route it appropriately.

Security reports do not belong in public issues. They go through GitHub's
private vulnerability reporting; see [../../SECURITY.md](../../SECURITY.md).

## Conventions

- **Create an issue**: `gh issue create --title "..." --body "..."`. Use a
  heredoc for multi-line bodies.
- **Read an issue**: `gh issue view <number> --comments`.
- **List issues**: `gh issue list --state open --json number,title,body,labels`
  with appropriate `--label` and `--state` filters.
- **Comment on an issue**: `gh issue comment <number> --body "..."`.
- **Apply or remove labels**: `gh issue edit <number> --add-label "..."` or
  `gh issue edit <number> --remove-label "..."`.
- **Close an issue**: `gh issue close <number> --comment "..."`.

Infer the repository from `git remote -v`; `gh` does this automatically when
run inside a clone.

## What belongs in an issue

An issue tracks a public report or request. It is not where accepted decisions
are recorded: an accepted architecture decision becomes an ADR under
[../adr/](../adr/), and the issue closes pointing at it. A rule the
implementation must follow belongs in the matching baseline document under
[../architecture/](../architecture/), not only in a comment thread.

## Pull requests as a request surface

Pull requests are not the primary request surface. GitHub shares one number
space across issues and pull requests, so resolve an ambiguous `#42` with
`gh pr view 42` and fall back to `gh issue view 42`.
