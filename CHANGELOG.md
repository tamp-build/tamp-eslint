# Changelog

All notable changes to `Tamp.Eslint.V9` are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.2.0] - 2026-11-20 - .NET 11 in, .NET 8 and .NET 9 out

### Changed

- **Multi-targets `net10.0;net11.0`** instead of `net8.0;net9.0;net10.0` ([ADR 0015](https://github.com/tamp-build/tamp/blob/main/docs/adr/0015-target-framework-strategy.md)). .NET 11 shipped 2026-11-10 and .NET 8 and .NET 9 reached end of support the same day; the rule is to add a TFM the day a release ships and drop one the day Microsoft ends its support. `global.json` pins SDK 11.0.100.

  **If you target `net8.0` or `net9.0`, stay on the previous version** — this one carries no assets your project can resolve.

- **`Tamp.*` pinned to 1.21.0**, which carries a SARIF fix worth knowing about if this project's output is read as evidence: before 1.21.0, a SARIF result whose scanner omitted `level` was read as `none` — the least severe value — instead of the declared `warning`, on every runtime before net11 ([tamp#166](https://github.com/tamp-build/tamp/issues/166)).

- **Publishing moved to nuget.org trusted publishing (OIDC).** The `tamp_nuget_api_key` org secret expired 2026-11-01 along with every other pre-2026-08-17 nuget.org key. The publish job now mints a short-lived key from its own OIDC token, so there is no long-lived credential to rotate or leak.

## [0.1.1] — 2026-09-27

### Added

- Initial release. Tamp wrapper for the ESLint v9 CLI with one verb (`Scan`):
  - `Eslint.Scan(Action<EslintScanSettings>)` and object-init overload.
  - Fluent / object-init settings authoring (parity per `Tamp.OpenGrep` / `Tamp.SqlPackage` pattern).
  - Knobs: `AddTarget`, `AddConfig`, `SetSarif`, `SetOutputFile`, `SetMaxWarnings`, `SetQuiet`, `SetFormat`, `SetWorkingDirectory`, `SetBinary`, `SetEnvironmentVariable`.
- `EslintBinaryResolver` — smart resolution in priority order: project-local `node_modules/.bin/eslint` → `pnpm exec eslint` (when `pnpm-lock.yaml` / `pnpm-workspace.yaml` present + `pnpm` on PATH) → `npm exec eslint` (when `package-lock.json` present + `npm` on PATH) → global `eslint` on PATH.
- `EslintBinaryResolver.IsAvailable(workingDirectory)` pre-flight for skippable security-pipeline targets.
- `EslintBinaryResolution` + `EslintResolutionSource` record / enum for explicit binary overrides and dashboard tagging.
- SARIF integration via `--format @microsoft/eslint-formatter-sarif`; output file enforced when `Sarif=true`.
- Multi-target `net8.0;net9.0;net10.0`.
- Package now ships XML documentation files (`.xml`) alongside the assembly, so consumers get IntelliSense and API docs. (Mirrors [tamp-build/tamp#3](https://github.com/tamp-build/tamp/pull/50).)

### Closes

- TAM-276 — Tamp.Eslint satellite — TS/JS inspection scanner for the security pipeline.
