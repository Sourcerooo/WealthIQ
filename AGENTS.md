# AGENTS.md
Guidance for agentic coding assistants working in `WealthIQ`.

## Scope and stack
- Language: C#
- SDK style projects
- Target framework: `net10.0`
- Solution: `WealthIQ.slnx`
- Nullable reference types: enabled
- Implicit usings: enabled
- Test framework: xUnit

## Repository layout
- `src/WealthIQ.Domain`
- `src/WealthIQ.Application`
- `src/WealthIQ.Infrastructure.IBKR`
- `src/WealthIQ.Cli`
- `tests/WealthIQ.Tests`

## Build, test, and format commands
Run from repository root.

### Restore
- `dotnet restore "WealthIQ.slnx"`

### Build
- `dotnet build "WealthIQ.slnx"`
- `dotnet build "src/WealthIQ.Domain/WealthIQ.Domain.csproj"`

### Clean + rebuild (when artifacts are stale or file locks happen)
- `dotnet clean "WealthIQ.slnx"`
- `dotnet build "WealthIQ.slnx"`

### Run tests
- `dotnet test "WealthIQ.slnx"`
- `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj"`

### Run a single test (important)
- By fully-qualified test name:
  - `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~WealthIQ.Tests.UnitTest1.Test1"`
- By class:
  - `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~WealthIQ.Tests.UnitTest1"`
- By display name substring:
  - `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "DisplayName~Test1"`

### Formatting / linting
- Check formatting: `dotnet format "WealthIQ.slnx" --verify-no-changes`
- Auto-fix formatting: `dotnet format "WealthIQ.slnx"`
- No separate linter is currently configured.

## Dependency direction (keep this strict)
- `Application -> Domain`
- `Infrastructure.IBKR -> Application, Domain`
- `Cli -> Application, Domain, Infrastructure.IBKR`
- `Tests -> Application, Domain`

Rules:
- Never introduce dependencies from `Domain` to outer layers.
- Keep business rules in `Domain` and `Application`.
- Keep broker/parsing concerns in `Infrastructure.IBKR`.

## Coding conventions
Use English for identifiers, docs, comments, and commit messages.

### Naming
- `PascalCase` for types, methods, public properties, enums
- `camelCase` for local variables and method parameters
- Prefer clear domain names (`Trade`, `Lot`, `Realization`, `Account`)
- Avoid unclear abbreviations unless already established

### File and namespace structure
- One primary public type per file
- File name matches the main type name
- Namespace aligns with folder path
- Avoid very large files and deep nesting

### Usings / imports
- Prefer implicit usings where enough
- Remove unused usings
- Keep `System.*` usings before project usings
- Avoid custom global usings unless centralized and agreed

### Formatting
- Follow `dotnet format`
- 4 spaces, no tabs
- Keep braces and whitespace consistent
- Keep lines readable

### Types and domain modeling
- Use `record` / `record struct` for immutable, value-centric models
- Use `readonly record struct` for tiny value objects where helpful
- Use `decimal` for money and quantities, never `double`
- Keep currency operations explicit and safe
- Use `DateOnly` / `DateTimeOffset` intentionally

### Nullability and validation
- Treat nullable warnings as real issues
- Validate external input at boundaries (CLI, API, XML)
- Avoid null-forgiving (`!`) unless justified
- Prefer guard clauses for invalid state

### Error handling
- Throw specific exceptions for invariant violations
- Include actionable error messages with identifiers/context
- Do not swallow exceptions
- For recoverable business outcomes, prefer structured results
- Preserve original exception context when wrapping

### Collections and immutability
- Use `IReadOnlyList<T>` across boundaries
- Avoid mutating collections passed by callers
- Prefer returning updated values rather than mutating shared state

### Testing conventions
- Add/update tests for every behavioral change
- Test names: `Method_Scenario_ExpectedResult`
- Cover happy path, edge cases, and invalid inputs
- Keep tests deterministic and explicit
- Prefer narrow, focused tests over broad opaque tests

## Domain-specific guardrails
- Default lot matching policy is FIFO unless changed intentionally
- Distinguish open lots from realized entries
- Partial closes must preserve remaining quantity and cost allocation
- Preserve provenance from realizations to source event slices
- Track gross/net results where the model supports it

## Agent workflow
- Inspect nearby files before making edits
- Match local style and naming
- Keep changes scoped; avoid drive-by refactors
- Update docs when behavior/contracts/commands change
- Run targeted tests for touched areas; run broader tests before handoff

## Cursor and Copilot rules
Checked locations:
- `.cursor/rules/`
- `.cursorrules`
- `.github/copilot-instructions.md`

Current status:
- No Cursor rules or Copilot instructions were found.
- If they appear later, treat them as higher-priority repository policy.

## Quick command copy list
- `dotnet restore "WealthIQ.slnx"`
- `dotnet build "WealthIQ.slnx"`
- `dotnet test "WealthIQ.slnx"`
- `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj"`
- `dotnet test "tests/WealthIQ.Tests/WealthIQ.Tests.csproj" --filter "FullyQualifiedName~WealthIQ.Tests.UnitTest1.Test1"`
- `dotnet format "WealthIQ.slnx" --verify-no-changes`

Keep this file current as structure, tooling, and standards evolve.
