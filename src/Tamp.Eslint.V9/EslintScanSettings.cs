namespace Tamp.Eslint.V9;

/// <summary>
/// Settings for an ESLint <c>scan</c> invocation: targets, configs, output, and
/// behavioral flags. Inherits the binary-resolution logic from
/// <see cref="EslintSettingsBase"/>.
/// </summary>
/// <remarks>
/// v1 exposes the scan-time flags adopters reach for in CI pipelines (targets,
/// configs, SARIF output, quiet, max-warnings). Niche flags (<c>--fix</c>,
/// <c>--rulesdir</c>, <c>--cache-*</c>) are intentionally out of scope and can
/// land as <c>SetXxx</c> additions when adopters ask. <c>--fix</c> in particular
/// is omitted on purpose because mutating source is not a behavior a security-scan
/// wrapper should silently enable.
/// </remarks>
public sealed class EslintScanSettings : EslintSettingsBase
{
    // No verb-specific extra properties yet; the base carries everything v1 needs.
    // When ESLint grows non-scan top-level verbs that justify separate settings
    // classes, branch this into a verb hierarchy mirroring Tamp.Trivy / Tamp.MsDeploy.
}

/// <summary>
/// Shared base for <c>eslint</c> invocations. Owns working directory, environment
/// variables, binary resolution, and the cross-cutting scan flags (targets, configs,
/// output, max-warnings, quiet).
/// </summary>
public abstract class EslintSettingsBase
{
    /// <summary>Working directory for the spawned process AND root for project-local binary resolution.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Per-invocation environment variables.</summary>
    public Dictionary<string, string> EnvironmentVariables { get; } = new();

    /// <summary>
    /// Explicit binary resolution. When null, <see cref="ToCommandPlan"/> auto-resolves
    /// via <see cref="EslintBinaryResolver.TryResolve"/> against <see cref="WorkingDirectory"/>.
    /// Set this to bypass auto-resolution (e.g. CI agent with a non-standard install path).
    /// </summary>
    public EslintBinaryResolution? Binary { get; set; }

    /// <summary>Files / directories / globs to scan. Required (at least one).</summary>
    public List<string> Targets { get; } = new();

    /// <summary>
    /// Config files to pass via <c>--config</c>. ESLint v9 uses flat config
    /// (<c>eslint.config.js</c>) by default; if you don't set this, ESLint
    /// auto-discovers a <c>eslint.config.js</c> in the working directory and
    /// its ancestors.
    /// </summary>
    public List<string> Configs { get; } = new();

    /// <summary>
    /// Emit SARIF 2.1.0 via <c>--format @microsoft/eslint-formatter-sarif</c>.
    /// Requires the formatter package in the project's devDependencies.
    /// </summary>
    public bool Sarif { get; set; }

    /// <summary>
    /// Output file (<c>--output-file</c>). Required when <see cref="Sarif"/> is true
    /// (SARIF is not useful streamed to stdout), optional otherwise.
    /// </summary>
    public string? OutputFile { get; set; }

    /// <summary>Maximum warnings allowed before ESLint exits non-zero (<c>--max-warnings</c>).</summary>
    public int? MaxWarnings { get; set; }

    /// <summary>Report only errors, suppress warnings (<c>--quiet</c>).</summary>
    public bool Quiet { get; set; }

    /// <summary>
    /// Override formatter when SARIF is not requested. Useful for human-readable
    /// CI output (e.g. <c>"stylish"</c>, <c>"compact"</c>) or alternative machine
    /// formats. Mutually overridden by <see cref="Sarif"/> when both set.
    /// </summary>
    public string? Format { get; set; }

    /// <summary>Cross-platform validation hook.</summary>
    protected virtual void Validate()
    {
        if (Targets.Count == 0)
            throw new InvalidOperationException("At least one target is required (set via AddTarget).");
        if (Sarif && string.IsNullOrEmpty(OutputFile))
            throw new InvalidOperationException("OutputFile is required when Sarif is true — SARIF isn't useful on stdout.");
    }

    /// <summary>Build the <see cref="CommandPlan"/> for this scan invocation.</summary>
    public CommandPlan ToCommandPlan()
    {
        Validate();

        var resolution = Binary
            ?? EslintBinaryResolver.TryResolve(WorkingDirectory ?? Directory.GetCurrentDirectory())
            ?? throw new InvalidOperationException(
                "ESLint not found via project-local / pnpm / npm / global resolution. " +
                "Install via `npm i -D eslint` (or pnpm equivalent), or pre-flight with " +
                "EslintBinaryResolver.IsAvailable(workingDirectory) and skip the scan target when false.");

        var args = new List<string>(resolution.PrefixArguments);

        // Configs first so the rendered command line reads naturally:
        //   eslint --config foo.js --format SARIF --output-file x.sarif src tests
        foreach (var config in Configs) { args.Add("--config"); args.Add(config); }

        if (Sarif)
        {
            args.Add("--format");
            args.Add("@microsoft/eslint-formatter-sarif");
        }
        else if (!string.IsNullOrEmpty(Format))
        {
            args.Add("--format");
            args.Add(Format!);
        }

        if (!string.IsNullOrEmpty(OutputFile)) { args.Add("--output-file"); args.Add(OutputFile!); }
        if (MaxWarnings is int mw) { args.Add("--max-warnings"); args.Add(mw.ToString(System.Globalization.CultureInfo.InvariantCulture)); }
        if (Quiet) args.Add("--quiet");

        // Targets last (positional).
        args.AddRange(Targets);

        return new CommandPlan
        {
            Executable = resolution.Executable,
            Arguments = args,
            Environment = new Dictionary<string, string>(EnvironmentVariables),
            WorkingDirectory = WorkingDirectory,
        };
    }
}

/// <summary>Fluent setters for the shared scan settings.</summary>
public static class EslintSettingsBaseExtensions
{
    public static T SetWorkingDirectory<T>(this T s, string? cwd) where T : EslintSettingsBase { s.WorkingDirectory = cwd; return s; }
    public static T SetEnvironmentVariable<T>(this T s, string name, string value) where T : EslintSettingsBase { s.EnvironmentVariables[name] = value; return s; }
    public static T SetBinary<T>(this T s, EslintBinaryResolution binary) where T : EslintSettingsBase { s.Binary = binary; return s; }
    public static T AddTarget<T>(this T s, string target) where T : EslintSettingsBase { s.Targets.Add(target); return s; }
    public static T AddConfig<T>(this T s, string config) where T : EslintSettingsBase { s.Configs.Add(config); return s; }
    public static T SetSarif<T>(this T s, bool v = true) where T : EslintSettingsBase { s.Sarif = v; return s; }
    public static T SetOutputFile<T>(this T s, string path) where T : EslintSettingsBase { s.OutputFile = path; return s; }
    public static T SetMaxWarnings<T>(this T s, int? n) where T : EslintSettingsBase { s.MaxWarnings = n; return s; }
    public static T SetQuiet<T>(this T s, bool v = true) where T : EslintSettingsBase { s.Quiet = v; return s; }
    public static T SetFormat<T>(this T s, string? format) where T : EslintSettingsBase { s.Format = format; return s; }
}
