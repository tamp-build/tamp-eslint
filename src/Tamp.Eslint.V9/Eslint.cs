namespace Tamp.Eslint.V9;

/// <summary>
/// Tamp wrapper for the <c>eslint</c> CLI (ESLint v9). Builds <see cref="CommandPlan"/>
/// instances for the <c>scan</c> verb (default — lint files / directories) with smart
/// binary resolution (project-local → pnpm/npm exec → global PATH).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Exit-code semantics.</strong> ESLint exits <c>0</c> on a clean run, <c>1</c>
/// when findings are reported, and <c>2</c> on a tool / config error. Adopters writing
/// security-pipeline targets should treat <c>0</c> and <c>1</c> as success (findings
/// are still successful scans) and only fail the target on <c>2+</c>. This mirrors the
/// <c>Tamp.OpenGrep</c> exit-code convention.
/// </para>
/// <para>
/// <strong>SARIF output.</strong> Set <c>SetSarif(true)</c> to emit SARIF 2.1.0 via
/// <c>@microsoft/eslint-formatter-sarif</c>. The formatter must be present in the
/// project's devDependencies — the wrapper just emits the <c>--format</c> flag.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// // Skippable pre-flight + fluent invocation.
/// if (!EslintBinaryResolver.IsAvailable(webRoot)) return;
///
/// var plan = Eslint.Scan(s => s
///     .AddTarget("src")
///     .AddTarget("tests")
///     .SetSarif()
///     .SetOutputFile("artifacts/security/eslint.sarif")
///     .SetQuiet()
///     .SetWorkingDirectory(webRoot));
///
/// var exit = ProcessRunner.Execute(plan);
/// if (exit > 1) throw new InvalidOperationException($"ESLint failed with exit {exit}.");
/// </code>
/// </example>
public static class Eslint
{
    /// <summary>Build a <c>eslint</c> scan CommandPlan via a fluent configure delegate.</summary>
    public static CommandPlan Scan(Action<EslintScanSettings> configure)
    {
        if (configure is null) throw new ArgumentNullException(nameof(configure));
        var settings = new EslintScanSettings();
        configure(settings);
        return settings.ToCommandPlan();
    }

    /// <summary>Object-init overload. Produces an identical CommandPlan to the fluent path.</summary>
    public static CommandPlan Scan(EslintScanSettings settings)
    {
        if (settings is null) throw new ArgumentNullException(nameof(settings));
        return settings.ToCommandPlan();
    }
}
