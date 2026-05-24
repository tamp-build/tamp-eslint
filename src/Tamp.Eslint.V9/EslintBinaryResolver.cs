namespace Tamp.Eslint.V9;

/// <summary>
/// How the <c>eslint</c> binary was located. Used to tag the resolved invocation so
/// adopters / dashboards can distinguish project-local installs from PATH fallbacks.
/// </summary>
public enum EslintResolutionSource
{
    /// <summary>
    /// Project-local install: <c>{workingDirectory}/node_modules/.bin/eslint</c>
    /// (or <c>eslint.cmd</c> on Windows). Preferred because the project's exact
    /// pinned ESLint + plugin versions run.
    /// </summary>
    ProjectLocal,

    /// <summary>
    /// pnpm exec: <c>pnpm exec eslint -- ...</c>. Used when a pnpm workspace marker
    /// (<c>pnpm-lock.yaml</c> or <c>pnpm-workspace.yaml</c>) is present at the working
    /// directory but <c>node_modules/.bin/eslint</c> hasn't been materialized yet.
    /// </summary>
    Pnpm,

    /// <summary>
    /// npm exec: <c>npm exec eslint -- ...</c>. Used when <c>package-lock.json</c>
    /// is present at the working directory.
    /// </summary>
    Npm,

    /// <summary>Global <c>eslint</c> on <c>PATH</c>.</summary>
    Global,

    /// <summary>Adopter passed an explicit <see cref="EslintBinaryResolution"/> via <c>Binary</c> / <c>SetBinary</c>.</summary>
    Explicit,
}

/// <summary>
/// A resolved ESLint invocation: the executable to spawn plus any prefix arguments
/// (e.g. <c>exec eslint --</c> for the pnpm/npm exec fallbacks). The wrapper threads
/// these into <see cref="CommandPlan.Arguments"/> before the per-scan flags.
/// </summary>
/// <remarks>
/// Constructed by <see cref="EslintBinaryResolver.TryResolve"/>, or hand-built by
/// adopters who want full control (e.g. a CI agent with a non-standard binary path).
/// </remarks>
public sealed record EslintBinaryResolution
{
    /// <summary>Absolute path to the executable to spawn.</summary>
    public required string Executable { get; init; }

    /// <summary>
    /// Arguments to emit BEFORE the per-scan flags. Empty for direct invocations
    /// (project-local, global). Non-empty for indirect invocations (pnpm/npm exec
    /// need <c>["exec", "eslint", "--"]</c>).
    /// </summary>
    public IReadOnlyList<string> PrefixArguments { get; init; } = Array.Empty<string>();

    /// <summary>How this resolution was produced.</summary>
    public required EslintResolutionSource Source { get; init; }
}

/// <summary>
/// Locates the ESLint CLI for a given working directory. Resolution priority:
/// <list type="number">
///   <item>Project-local: <c>{workingDirectory}/node_modules/.bin/eslint</c>.</item>
///   <item>pnpm exec: if <c>pnpm-lock.yaml</c> or <c>pnpm-workspace.yaml</c> exists at the working directory AND <c>pnpm</c> is on <c>PATH</c>.</item>
///   <item>npm exec: if <c>package-lock.json</c> exists at the working directory AND <c>npm</c> is on <c>PATH</c>.</item>
///   <item>Global <c>eslint</c> on <c>PATH</c>.</item>
///   <item>null — adopter should skip the scan target (see <see cref="IsAvailable"/>).</item>
/// </list>
/// </summary>
/// <remarks>
/// Resolution is performed at <see cref="Eslint.Scan(System.Action{EslintScanSettings})"/>
/// time when no <see cref="EslintBinaryResolution"/> is set. Adopters who want to
/// pre-flight (and skip the scan target cleanly when ESLint isn't installed) call
/// <see cref="IsAvailable"/> before invoking the wrapper.
/// </remarks>
public static class EslintBinaryResolver
{
    /// <summary>
    /// Resolve the ESLint invocation for <paramref name="workingDirectory"/>, or return
    /// <c>null</c> if no install is found.
    /// </summary>
    public static EslintBinaryResolution? TryResolve(string workingDirectory)
    {
        if (string.IsNullOrEmpty(workingDirectory))
            throw new ArgumentException("workingDirectory is required.", nameof(workingDirectory));

        // 1. Project-local node_modules/.bin/eslint
        var localBin = Path.Combine(workingDirectory, "node_modules", ".bin", LocalBinName);
        if (File.Exists(localBin))
        {
            return new EslintBinaryResolution
            {
                Executable = localBin,
                Source = EslintResolutionSource.ProjectLocal,
            };
        }

        // 2. pnpm exec — pnpm workspace marker present + pnpm on PATH
        if (HasPnpmWorkspaceMarker(workingDirectory))
        {
            var pnpm = ResolveOnPath("pnpm") ?? ResolveOnPath("pnpm.cmd");
            if (pnpm is not null)
            {
                return new EslintBinaryResolution
                {
                    Executable = pnpm,
                    PrefixArguments = new[] { "exec", "eslint", "--" },
                    Source = EslintResolutionSource.Pnpm,
                };
            }
        }

        // 3. npm exec — package-lock.json present + npm on PATH
        if (File.Exists(Path.Combine(workingDirectory, "package-lock.json")))
        {
            var npm = ResolveOnPath("npm") ?? ResolveOnPath("npm.cmd");
            if (npm is not null)
            {
                return new EslintBinaryResolution
                {
                    Executable = npm,
                    PrefixArguments = new[] { "exec", "eslint", "--" },
                    Source = EslintResolutionSource.Npm,
                };
            }
        }

        // 4. Global eslint on PATH
        var global = ResolveOnPath(GlobalBinName);
        if (global is not null)
        {
            return new EslintBinaryResolution
            {
                Executable = global,
                Source = EslintResolutionSource.Global,
            };
        }

        return null;
    }

    /// <summary>
    /// True when <see cref="TryResolve"/> finds an ESLint install for <paramref name="workingDirectory"/>.
    /// Use this to pre-flight a security-pipeline target and skip cleanly when ESLint isn't installed.
    /// </summary>
    /// <example>
    /// <code>
    /// protected virtual Target SecurityScanEslint => _ => _
    ///     .Executes(() =>
    ///     {
    ///         if (!EslintBinaryResolver.IsAvailable(WebRoot))
    ///         {
    ///             Log.Info("[security] ESLint skipped — not installed at {Dir}", WebRoot);
    ///             return;
    ///         }
    ///         var plan = Eslint.Scan(s => s.AddTarget("src").SetSarif().SetOutputFile(...));
    ///         ProcessRunner.Execute(plan);
    ///     });
    /// </code>
    /// </example>
    public static bool IsAvailable(string workingDirectory) => TryResolve(workingDirectory) is not null;

    private static string LocalBinName => OperatingSystem.IsWindows() ? "eslint.cmd" : "eslint";
    private static string GlobalBinName => OperatingSystem.IsWindows() ? "eslint.cmd" : "eslint";

    private static bool HasPnpmWorkspaceMarker(string workingDirectory) =>
        File.Exists(Path.Combine(workingDirectory, "pnpm-lock.yaml")) ||
        File.Exists(Path.Combine(workingDirectory, "pnpm-workspace.yaml"));

    private static string? ResolveOnPath(string binary)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path)) return null;
        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        foreach (var dir in path.Split(separator))
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;
            string candidate;
            try { candidate = Path.Combine(dir, binary); }
            catch (ArgumentException) { continue; }
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
