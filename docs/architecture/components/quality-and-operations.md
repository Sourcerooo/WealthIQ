# Quality And Operations

## Quality Direction In The Current Repository

- The solution targets `net10.0` with nullable reference types and implicit usings enabled.
- Tests use xUnit.
- `dotnet build "WealthIQ.slnx"` and `dotnet test "WealthIQ.slnx"` are the primary validation commands.
- `dotnet format "WealthIQ.slnx" --verify-no-changes` is the formatting check.

## Current Testing Strategy

- Domain tests focus on lot invariants and value behavior.
- Application tests focus on FIFO matching and German tax calculation behavior.
- Regression coverage exercises the import and tax pipeline against historical sample input.

## Operational Characteristics Of The Current Slice

- Import reads local XML files only.
- Tax reference data is file-based and local.
- The CLI prints human-readable console output and returns a non-zero exit code on fatal import diagnostics or unhandled exceptions.
- No CI, packaging, persistence, or deployment process is documented in the current repository-level docs.
