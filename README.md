# Tamp.Eslint.V9

> Tamp wrapper for the ESLint v9 CLI. Pattern + style + best-practice linter for TypeScript and JavaScript. Emits SARIF via `@microsoft/eslint-formatter-sarif` for the Tamp.Sarif ingest chain. Smart binary resolution: project-local → pnpm exec → npm exec → global PATH → pre-flight skip.

| Package | Status |
|---|---|
| `Tamp.Eslint.V9` | 0.1.0 (initial) |

## Install

```bash
dotnet add package Tamp.Eslint.V9
```

Multi-targets net8 / net9 / net10. The wrapper itself is .NET; it shells out to the ESLint CLI which lives in your project (or globally). See [Binary resolution](#binary-resolution) for how the wrapper finds it.

## Why this exists (and what the existing scanners miss)

The Tamp security pipeline already covers C# via Roslyn analyzers (semantic) and ReSharper InspectCode (style + best-practice). For pattern-based security scanning of TS/JS, `Tamp.OpenGrep` covers the security-rule families (`p/typescript`, `p/security-audit`, `p/owasp-top-ten`, etc.).

What's still missing for TypeScript / JavaScript codebases: the equivalent of "ReSharper for C#" — a linter that flags style + best-practice issues (unused vars, missing `useEffect` deps, prefer-const, no-explicit-any, unstable hook ordering, etc.). That's ESLint's domain, and that's what this wrapper enables.

## Quick start

### Skippable pre-flight (recommended for security pipelines)

```csharp
using Tamp;
using Tamp.Eslint.V9;

[Solution] readonly Solution Solution = null!;
AbsolutePath WebRoot => RootDirectory / "web";

Target SecurityScanEslint => _ => _
    .Description("TS/JS style + best-practice via ESLint v9.")
    .Executes(() =>
    {
        if (!EslintBinaryResolver.IsAvailable(WebRoot))
        {
            Log.Info("[security] ESLint skipped — no install found at {Dir}", WebRoot);
            return;
        }

        var plan = Eslint.Scan(s => s
            .SetWorkingDirectory(WebRoot)
            .AddTarget("src")
            .AddTarget("tests")
            .SetSarif()
            .SetOutputFile(RootDirectory / "artifacts" / "security" / "eslint.sarif")
            .SetMaxWarnings(0)
            .SetQuiet());

        var exit = ProcessRunner.Execute(plan);
        // ESLint: 0 = clean, 1 = findings, 2+ = tool/config error.
        if (exit > 1) throw new InvalidOperationException($"ESLint failed with exit {exit}.");
    });
```

### Direct invocation (when you know ESLint is installed)

```csharp
var plan = Eslint.Scan(s => s
    .AddTarget("web/src")
    .SetSarif()
    .SetOutputFile("artifacts/security/eslint.sarif")
    .SetWorkingDirectory("web")
    .SetQuiet());

ProcessRunner.Execute(plan);
```

## Verb surface (v1)

| Verb | Wraps | Required |
|---|---|---|
| `Scan` | `eslint <flags> <targets>` | At least one `AddTarget(...)`; `OutputFile` required when `Sarif` is true |

Out of scope for v1 (file as follow-up tickets if adopters ask):

- `--fix` / autofix — mutating source from a security-scan wrapper is a foot-gun; explicit opt-in only.
- Caching flags (`--cache`, `--cache-location`) — adopter-side concern.
- `--rulesdir` — non-flat-config legacy mechanism.
- Other formatters beyond the SARIF + arbitrary string override.

## Binary resolution

ESLint isn't shipped through `dotnet tool` or any Tamp-native install attribute. The wrapper resolves the binary at `Scan(...)` time in priority order:

1. **Project-local** — `{WorkingDirectory}/node_modules/.bin/eslint` (or `eslint.cmd` on Windows). Preferred because the project's exact pinned ESLint + plugin versions run.
2. **pnpm exec** — `pnpm exec eslint -- ...`. Used when `pnpm-lock.yaml` or `pnpm-workspace.yaml` is present at `WorkingDirectory` AND `pnpm` is on `PATH`.
3. **npm exec** — `npm exec eslint -- ...`. Used when `package-lock.json` is present at `WorkingDirectory` AND `npm` is on `PATH`.
4. **Global** — `eslint` on `PATH`.
5. **Not found** — `ToCommandPlan()` throws `InvalidOperationException` with an actionable message.

### Pre-flighting (skip when not installed)

```csharp
if (!EslintBinaryResolver.IsAvailable(workingDirectory))
{
    // Log and skip the target — same posture as the rest of the security pipeline.
    return;
}
```

### Overriding resolution explicitly

```csharp
var binary = new EslintBinaryResolution
{
    Executable = "/opt/ci/eslint/9.30.0/eslint",
    Source = EslintResolutionSource.Explicit,
};

var plan = Eslint.Scan(s => s
    .SetBinary(binary)
    .AddTarget("src"));
```

## SARIF integration

Set `SetSarif(true)` to emit SARIF 2.1.0 via `@microsoft/eslint-formatter-sarif`. The formatter is a separate npm package that must be present in the project's devDependencies:

```bash
# In your web project root:
pnpm add -D @microsoft/eslint-formatter-sarif
# or
npm install -D @microsoft/eslint-formatter-sarif
```

The wrapper just emits `--format @microsoft/eslint-formatter-sarif --output-file <path>`. The Tamp.Sarif reader ingests the produced SARIF unchanged.

## Exit-code semantics

ESLint follows the standard linter convention:

| Exit | Meaning | Treat as |
|---|---|---|
| `0` | Clean run, no findings | success |
| `1` | Findings reported | success (findings are still a successful scan) |
| `2+` | Tool or config error | failure |

A security-pipeline target should only throw on `exit > 1`. This mirrors the `Tamp.OpenGrep` exit-code convention.

## Settings authoring — fluent or object-init

Both produce identical `CommandPlan`s; fluent is canonical in docs.

```csharp
// Fluent
Eslint.Scan(s => s
    .AddTarget("src")
    .SetSarif()
    .SetOutputFile("out.sarif"));

// Object-init
var settings = new EslintScanSettings
{
    Sarif = true,
    OutputFile = "out.sarif",
};
settings.Targets.Add("src");
Eslint.Scan(settings);
```

## Minimal ESLint v9 flat-config example for TypeScript + React

For reference — adopters typically already have an `eslint.config.js`. If you don't:

```javascript
// eslint.config.js — at the root of your web/ project
import js from '@eslint/js';
import tseslint from 'typescript-eslint';
import react from 'eslint-plugin-react';
import reactHooks from 'eslint-plugin-react-hooks';

export default [
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    files: ['**/*.{ts,tsx}'],
    plugins: {
      react,
      'react-hooks': reactHooks,
    },
    rules: {
      ...react.configs.recommended.rules,
      ...reactHooks.configs.recommended.rules,
      'react/react-in-jsx-scope': 'off',
    },
    settings: { react: { version: 'detect' } },
  },
];
```

devDependencies:

```bash
pnpm add -D eslint @eslint/js typescript-eslint \
            eslint-plugin-react eslint-plugin-react-hooks \
            @microsoft/eslint-formatter-sarif
```

## Why not Biome / OXLint instead

Both are faster and ergonomically nicer. Open to either as additional satellites later (`Tamp.Biome`, `Tamp.OxLint`). ESLint is the incumbent with the largest rule ecosystem (`@typescript-eslint`, `eslint-plugin-react`, thousands of community plugins) — best baseline for first coverage.

## License

MIT — see [LICENSE](LICENSE).
