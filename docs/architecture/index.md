# Architecture Documentation Index

This index describes how the WealthIQ architecture documentation is organized and which document should be used for which question.

## Reading Order

Use this reading order unless a task is scoped to one specific component.

1. `docs/Architecture.md`
2. `docs/architecture/glossary.md`
3. Relevant file in `docs/architecture/components/`
4. Relevant file in `docs/architecture/design/`
5. Optional example in `docs/architecture/examples/`
6. `docs/architecture/current-state.md`
7. `docs/architecture/open-questions.md`

## Which Document To Use

| Question | Read |
|---|---|
| What is the current product slice and high-level architecture? | `docs/Architecture.md` |
| What does a component own and what must it not own? | `docs/architecture/components/*.md` |
| Which contracts, algorithms, and data flows are implemented? | `docs/architecture/design/*.md` |
| Which terms are canonical? | `docs/architecture/glossary.md` |
| What is already implemented and where are the maturity gaps? | `docs/architecture/current-state.md` |
| Which repository-level questions are still unresolved? | `docs/architecture/open-questions.md` |
| Is something illustrative rather than normative? | Check whether the file is in `docs/architecture/examples/` |

## Directory Map

```text
docs/
  Architecture.md
  Vision.md
  Roadmap.md
  Todo.md
  DoneTasks.md
  architecture/
    index.md
    glossary.md
    current-state.md
    open-questions.md
    components/
      application.md
      delivery.md
      domain.md
      infrastructure.md
      quality-and-operations.md
    design/
      application-contracts.md
      canonical-portfolio-ledger.md
      cli-tax-reporting.md
      german-tax-calculation.md
      import-pipeline.md
      lot-matching.md
      tax-reference-data.md
    examples/
      end-to-end-tax-report-flow.md
```

## Document Rules

- `Architecture.md` stays short and stable.
- `components/` defines responsibilities and boundaries, not method-by-method implementation detail.
- `design/` defines technical contracts, algorithm boundaries, state ownership, and main data flows.
- `examples/` is informative only and must not be treated as the sole normative source.
- `current-state.md` describes maturity and scope without turning into a task tracker.
- `open-questions.md` captures unresolved questions without guessing the answer.
