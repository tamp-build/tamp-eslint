# Changelog

All notable changes to `Tamp.Eslint.V9` are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
