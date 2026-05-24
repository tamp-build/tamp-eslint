using System.Linq;
using Tamp.Eslint.V9;
using Xunit;

namespace Tamp.Eslint.V9.Tests;

/// <summary>
/// Filesystem-based tests for <see cref="EslintBinaryResolver"/>. Each test stands
/// up a temp directory with the markers / files that simulate a real project
/// layout, then asserts the resolution outcome. PATH-based resolution is exercised
/// by stubbing PATH to a temp directory.
/// </summary>
// Serialize with EslintTests — both mutate process-wide PATH.
[Collection("PathSensitive")]
public sealed class EslintBinaryResolverTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string? _originalPath;

    public EslintBinaryResolverTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "tamp-eslint-resolver-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempRoot);
        _originalPath = Environment.GetEnvironmentVariable("PATH");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("PATH", _originalPath);
        try { Directory.Delete(_tempRoot, recursive: true); } catch { /* best effort */ }
    }

    private string MakeProject(string subdir)
    {
        var dir = Path.Combine(_tempRoot, subdir);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string BinaryName => OperatingSystem.IsWindows() ? "eslint.cmd" : "eslint";
    private static string PnpmName => OperatingSystem.IsWindows() ? "pnpm.cmd" : "pnpm";
    private static string NpmName => OperatingSystem.IsWindows() ? "npm.cmd" : "npm";

    private string PutExecutable(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, OperatingSystem.IsWindows() ? "@echo off\r\necho fake\r\n" : "#!/bin/sh\necho fake\n");
        if (!OperatingSystem.IsWindows())
        {
            // Mark executable so File.Exists semantics on POSIX work.
            try { System.Diagnostics.Process.Start("chmod", $"+x \"{path}\"")?.WaitForExit(); } catch { /* best effort */ }
        }
        return path;
    }

    // ---- Project-local resolution ----

    [Fact]
    public void Resolves_ProjectLocal_When_node_modules_bin_eslint_Exists()
    {
        var project = MakeProject("with-local-eslint");
        Directory.CreateDirectory(Path.Combine(project, "node_modules", ".bin"));
        var localBin = Path.Combine(project, "node_modules", ".bin", BinaryName);
        File.WriteAllText(localBin, "fake");

        var resolved = EslintBinaryResolver.TryResolve(project);

        Assert.NotNull(resolved);
        Assert.Equal(EslintResolutionSource.ProjectLocal, resolved!.Source);
        Assert.Equal(localBin, resolved.Executable);
        Assert.Empty(resolved.PrefixArguments);
    }

    [Fact]
    public void ProjectLocal_Beats_PnpmExec_When_Both_Available()
    {
        var project = MakeProject("local-and-pnpm");
        Directory.CreateDirectory(Path.Combine(project, "node_modules", ".bin"));
        var localBin = Path.Combine(project, "node_modules", ".bin", BinaryName);
        File.WriteAllText(localBin, "fake");
        File.WriteAllText(Path.Combine(project, "pnpm-lock.yaml"), "lockfileVersion: '6.0'");

        // pnpm on PATH too
        var pathStub = MakeProject("path-stub-pnpm");
        PutExecutable(pathStub, PnpmName);
        Environment.SetEnvironmentVariable("PATH", pathStub);

        var resolved = EslintBinaryResolver.TryResolve(project);

        Assert.NotNull(resolved);
        Assert.Equal(EslintResolutionSource.ProjectLocal, resolved!.Source);
        Assert.Equal(localBin, resolved.Executable);
    }

    // ---- pnpm exec resolution ----

    [Fact]
    public void Resolves_Pnpm_Exec_When_pnpm_lock_And_pnpm_On_PATH()
    {
        var project = MakeProject("pnpm-project");
        File.WriteAllText(Path.Combine(project, "pnpm-lock.yaml"), "lockfileVersion: '6.0'");

        var pathStub = MakeProject("path-stub-pnpm-2");
        var pnpmBin = PutExecutable(pathStub, PnpmName);
        Environment.SetEnvironmentVariable("PATH", pathStub);

        var resolved = EslintBinaryResolver.TryResolve(project);

        Assert.NotNull(resolved);
        Assert.Equal(EslintResolutionSource.Pnpm, resolved!.Source);
        Assert.Equal(pnpmBin, resolved.Executable);
        Assert.Equal(new[] { "exec", "eslint", "--" }, resolved.PrefixArguments.ToArray());
    }

    [Fact]
    public void Resolves_Pnpm_Exec_From_pnpm_workspace_Yaml_Marker_Too()
    {
        var project = MakeProject("pnpm-workspace");
        File.WriteAllText(Path.Combine(project, "pnpm-workspace.yaml"), "packages:\n  - 'packages/*'");

        var pathStub = MakeProject("path-stub-pnpm-3");
        PutExecutable(pathStub, PnpmName);
        Environment.SetEnvironmentVariable("PATH", pathStub);

        var resolved = EslintBinaryResolver.TryResolve(project);
        Assert.Equal(EslintResolutionSource.Pnpm, resolved!.Source);
    }

    [Fact]
    public void Pnpm_Marker_Without_Pnpm_On_PATH_Falls_Through()
    {
        var project = MakeProject("pnpm-no-binary");
        File.WriteAllText(Path.Combine(project, "pnpm-lock.yaml"), "lockfileVersion: '6.0'");

        // PATH points to an empty directory — no pnpm, no eslint
        var pathStub = MakeProject("path-stub-empty");
        Environment.SetEnvironmentVariable("PATH", pathStub);

        var resolved = EslintBinaryResolver.TryResolve(project);
        // Falls through to null (no global eslint either)
        Assert.Null(resolved);
    }

    // ---- npm exec resolution ----

    [Fact]
    public void Resolves_Npm_Exec_When_package_lock_And_npm_On_PATH()
    {
        var project = MakeProject("npm-project");
        File.WriteAllText(Path.Combine(project, "package-lock.json"), "{}");

        var pathStub = MakeProject("path-stub-npm");
        var npmBin = PutExecutable(pathStub, NpmName);
        Environment.SetEnvironmentVariable("PATH", pathStub);

        var resolved = EslintBinaryResolver.TryResolve(project);

        Assert.NotNull(resolved);
        Assert.Equal(EslintResolutionSource.Npm, resolved!.Source);
        Assert.Equal(npmBin, resolved.Executable);
        Assert.Equal(new[] { "exec", "eslint", "--" }, resolved.PrefixArguments.ToArray());
    }

    [Fact]
    public void Pnpm_Beats_Npm_When_Both_Markers_Present()
    {
        // Mixed project (legacy lockfiles): pnpm marker takes precedence.
        var project = MakeProject("pnpm-and-npm");
        File.WriteAllText(Path.Combine(project, "pnpm-lock.yaml"), "lockfileVersion: '6.0'");
        File.WriteAllText(Path.Combine(project, "package-lock.json"), "{}");

        var pathStub = MakeProject("path-stub-both");
        PutExecutable(pathStub, PnpmName);
        PutExecutable(pathStub, NpmName);
        Environment.SetEnvironmentVariable("PATH", pathStub);

        var resolved = EslintBinaryResolver.TryResolve(project);
        Assert.Equal(EslintResolutionSource.Pnpm, resolved!.Source);
    }

    // ---- Global PATH resolution ----

    [Fact]
    public void Resolves_Global_eslint_From_PATH_When_No_Project_Markers()
    {
        var project = MakeProject("no-markers");
        var pathStub = MakeProject("path-stub-global");
        var globalBin = PutExecutable(pathStub, BinaryName);
        Environment.SetEnvironmentVariable("PATH", pathStub);

        var resolved = EslintBinaryResolver.TryResolve(project);

        Assert.NotNull(resolved);
        Assert.Equal(EslintResolutionSource.Global, resolved!.Source);
        Assert.Equal(globalBin, resolved.Executable);
        Assert.Empty(resolved.PrefixArguments);
    }

    // ---- No-resolution paths ----

    [Fact]
    public void TryResolve_Returns_Null_When_Nothing_Found()
    {
        var project = MakeProject("empty");
        // PATH stub with nothing useful
        Environment.SetEnvironmentVariable("PATH", MakeProject("path-stub-nothing"));

        var resolved = EslintBinaryResolver.TryResolve(project);
        Assert.Null(resolved);
    }

    [Fact]
    public void IsAvailable_True_When_TryResolve_Non_Null()
    {
        var project = MakeProject("for-available-true");
        var pathStub = MakeProject("path-stub-avail-true");
        PutExecutable(pathStub, BinaryName);
        Environment.SetEnvironmentVariable("PATH", pathStub);

        Assert.True(EslintBinaryResolver.IsAvailable(project));
    }

    [Fact]
    public void IsAvailable_False_When_Nothing_Resolves()
    {
        var project = MakeProject("for-available-false");
        Environment.SetEnvironmentVariable("PATH", MakeProject("path-stub-avail-false"));

        Assert.False(EslintBinaryResolver.IsAvailable(project));
    }

    // ---- Argument validation ----

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void TryResolve_With_Empty_WorkingDirectory_Throws(string? cwd)
    {
        Assert.Throws<ArgumentException>(() => EslintBinaryResolver.TryResolve(cwd!));
    }
}
