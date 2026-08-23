# Domain Docs

How to consume this repository's domain documentation when exploring the
codebase.

## Before exploring, read these

- **`CONTEXT.md`** at the repository root. This project uses its domain terms
  precisely, and several of them are deliberately narrower than their everyday
  meaning. `Payment`, `Observed Payment`, `Matching Blockchain Transaction`,
  `Settlement`, and `Blockchain Truth` all name specific things, and the file
  also records the words to *avoid* — using "transaction" for a Payment or
  "customer" for a Payer will make code and conversation drift apart.
- **`docs/adr/`** — read the ADRs that touch the area you are about to work in.
  They explain why the obvious alternative was not taken, which is usually the
  thing that is not visible from the code.
- **`docs/architecture/`** — the baseline document for that area states what the
  implementation has to do. Where a baseline and an ADR disagree, the ADR is the
  decision and the baseline is out of date.

This is a **single-context** repository:

```
/
├── CONTEXT.md
├── VISION.md
├── docs/adr/
└── docs/architecture/
```

## When a term is resolved

A new or sharpened domain term goes into `CONTEXT.md` with the alternatives it
replaces. A decision that was genuinely a trade-off goes into `docs/adr/` in the
form described in [../adr/README.md](../adr/README.md) — a title that states the
decision, prose that names the rejected alternative, and consequences only where
they add something.

Not every choice is an ADR. Product scope belongs in
[../../VISION.md](../../VISION.md), and detailed product rules belong in
[../product/requirements.md](../product/requirements.md).
