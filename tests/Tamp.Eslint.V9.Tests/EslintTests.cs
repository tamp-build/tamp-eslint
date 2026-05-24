using System.Linq;
using Bogus;
using Tamp;
using Tamp.Eslint.V9;
using Xunit;

namespace Tamp.Eslint.V9.Tests;

// Serialize with EslintBinaryResolverTests — both mutate process-wide PATH.
[Collection("PathSensitive")]
public sealed class EslintTests
{
    /// <summary>Fixed explicit binary so tests don't depend on a real ESLint install on the runner.</summary>
    private static EslintBinaryResolution FakeBinary() => new()
    {
        Executable = OperatingSystem.IsWindows() ? "C:\\fake\\eslint.cmd" : "/fake/eslint",
        Source = EslintResolutionSource.Explicit,
    };

    private static int IndexOf(IReadOnlyList<string> args, string token)
    {
        for (var i = 0; i < args.Count; i++) if (args[i] == token) return i;
        return -1;
    }

    // ---- Targets ----

    [Fact]
    public void Scan_Targets_Are_Positional_At_End()
    {
        var plan = Eslint.Scan(s => s
            .SetBinary(FakeBinary())
            .AddTarget("src"));

        Assert.Equal("src", plan.Arguments[^1]);
    }

    [Fact]
    public void Scan_Multiple_Targets_All_Emit_In_Order()
    {
        var plan = Eslint.Scan(s => s
            .SetBinary(FakeBinary())
            .AddTarget("src")
            .AddTarget("tests")
            .AddTarget("packages/web"));

        var args = plan.Arguments;
        Assert.Equal(new[] { "src", "tests", "packages/web" }, args.Skip(args.Count - 3).ToArray());
    }

    [Fact]
    public void Scan_Without_Targets_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Eslint.Scan(s => s.SetBinary(FakeBinary())));
        Assert.Contains("target", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Configs ----

    [Fact]
    public void Single_Config_Emits_config_Flag()
    {
        var plan = Eslint.Scan(s => s
            .SetBinary(FakeBinary())
            .AddTarget("src")
            .AddConfig("eslint.config.js"));

        var idx = IndexOf(plan.Arguments, "--config");
        Assert.True(idx >= 0);
        Assert.Equal("eslint.config.js", plan.Arguments[idx + 1]);
    }

    [Fact]
    public void Multiple_Configs_Each_Get_Their_Own_config_Flag()
    {
        var plan = Eslint.Scan(s => s
            .SetBinary(FakeBinary())
            .AddTarget("src")
            .AddConfig("eslint.base.js")
            .AddConfig("eslint.security.js"));

        Assert.Equal(2, plan.Arguments.Count(a => a == "--config"));
        Assert.Contains("eslint.base.js", plan.Arguments);
        Assert.Contains("eslint.security.js", plan.Arguments);
    }

    // ---- SARIF formatter ----

    [Fact]
    public void Sarif_True_With_OutputFile_Emits_Format_And_OutputFile()
    {
        var plan = Eslint.Scan(s => s
            .SetBinary(FakeBinary())
            .AddTarget("src")
            .SetSarif()
            .SetOutputFile("artifacts/security/eslint.sarif"));

        var formatIdx = IndexOf(plan.Arguments, "--format");
        Assert.Equal("@microsoft/eslint-formatter-sarif", plan.Arguments[formatIdx + 1]);

        var outIdx = IndexOf(plan.Arguments, "--output-file");
        Assert.Equal("artifacts/security/eslint.sarif", plan.Arguments[outIdx + 1]);
    }

    [Fact]
    public void Sarif_True_Without_OutputFile_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Eslint.Scan(s => s
            .SetBinary(FakeBinary())
            .AddTarget("src")
            .SetSarif()));

        Assert.Contains("OutputFile", ex.Message);
        Assert.Contains("Sarif", ex.Message);
    }

    [Fact]
    public void Sarif_False_Format_Custom_Emits_Custom_Formatter()
    {
        var plan = Eslint.Scan(s => s
            .SetBinary(FakeBinary())
            .AddTarget("src")
            .SetFormat("stylish"));

        var formatIdx = IndexOf(plan.Arguments, "--format");
        Assert.Equal("stylish", plan.Arguments[formatIdx + 1]);
    }

    [Fact]
    public void Sarif_True_Overrides_Custom_Format()
    {
        // SARIF takes precedence; the SARIF formatter is what gets emitted.
        var plan = Eslint.Scan(s => s
            .SetBinary(FakeBinary())
            .AddTarget("src")
            .SetFormat("stylish")
            .SetSarif()
            .SetOutputFile("out.sarif"));

        Assert.Single(plan.Arguments, a => a == "--format");
        var formatIdx = IndexOf(plan.Arguments, "--format");
        Assert.Equal("@microsoft/eslint-formatter-sarif", plan.Arguments[formatIdx + 1]);
        Assert.DoesNotContain("stylish", plan.Arguments);
    }

    // ---- Behavioral flags ----

    [Fact]
    public void Quiet_Emits_quiet_Flag()
    {
        var plan = Eslint.Scan(s => s.SetBinary(FakeBinary()).AddTarget("src").SetQuiet());
        Assert.Contains("--quiet", plan.Arguments);
    }

    [Fact]
    public void Quiet_Default_Omits_Flag()
    {
        var plan = Eslint.Scan(s => s.SetBinary(FakeBinary()).AddTarget("src"));
        Assert.DoesNotContain("--quiet", plan.Arguments);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(100)]
    public void MaxWarnings_Emits_Flag_With_Value(int n)
    {
        var plan = Eslint.Scan(s => s.SetBinary(FakeBinary()).AddTarget("src").SetMaxWarnings(n));
        var idx = IndexOf(plan.Arguments, "--max-warnings");
        Assert.Equal(n.ToString(System.Globalization.CultureInfo.InvariantCulture), plan.Arguments[idx + 1]);
    }

    [Fact]
    public void MaxWarnings_Null_Omits_Flag()
    {
        var plan = Eslint.Scan(s => s.SetBinary(FakeBinary()).AddTarget("src"));
        Assert.DoesNotContain("--max-warnings", plan.Arguments);
    }

    // ---- Binary resolution ----

    [Fact]
    public void Binary_Executable_Flows_To_CommandPlan_Executable()
    {
        var plan = Eslint.Scan(s => s.SetBinary(FakeBinary()).AddTarget("src"));
        Assert.Equal(FakeBinary().Executable, plan.Executable);
    }

    [Fact]
    public void Binary_PrefixArguments_Lead_Arg_List()
    {
        var binary = new EslintBinaryResolution
        {
            Executable = "/usr/local/bin/pnpm",
            PrefixArguments = new[] { "exec", "eslint", "--" },
            Source = EslintResolutionSource.Pnpm,
        };

        var plan = Eslint.Scan(s => s.SetBinary(binary).AddTarget("src").SetSarif().SetOutputFile("out.sarif"));

        Assert.Equal("/usr/local/bin/pnpm", plan.Executable);
        Assert.Equal(new[] { "exec", "eslint", "--" }, plan.Arguments.Take(3).ToArray());
        // SARIF format follows the prefix, then targets at the end.
        Assert.Contains("--format", plan.Arguments);
        Assert.Equal("src", plan.Arguments[^1]);
    }

    [Fact]
    public void No_Binary_And_No_Auto_Resolve_Throws_With_Actionable_Message()
    {
        // No FakeBinary set, working directory is a temp dir with no ESLint anywhere.
        // Stub PATH to an empty temp dir so the test is hermetic (some CI runners
        // may have eslint preinstalled globally — we don't want that to mask the failure).
        var tempDir = Path.Combine(Path.GetTempPath(), "tamp-eslint-tests-" + Guid.NewGuid().ToString("N")[..8]);
        var emptyPath = Path.Combine(Path.GetTempPath(), "tamp-eslint-emptypath-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        Directory.CreateDirectory(emptyPath);
        var originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            Environment.SetEnvironmentVariable("PATH", emptyPath);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                Eslint.Scan(s => s.AddTarget("src").SetWorkingDirectory(tempDir)));

            Assert.Contains("ESLint not found", ex.Message);
            Assert.Contains("IsAvailable", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            try { Directory.Delete(tempDir, recursive: true); } catch { /* best effort */ }
            try { Directory.Delete(emptyPath, recursive: true); } catch { /* best effort */ }
        }
    }

    // ---- Working directory + env vars ----

    [Fact]
    public void WorkingDirectory_Propagates_To_CommandPlan()
    {
        var plan = Eslint.Scan(s => s
            .SetBinary(FakeBinary())
            .AddTarget("src")
            .SetWorkingDirectory("/tmp/web"));
        Assert.Equal("/tmp/web", plan.WorkingDirectory);
    }

    [Fact]
    public void Environment_Variables_Propagate()
    {
        var plan = Eslint.Scan(s => s
            .SetBinary(FakeBinary())
            .AddTarget("src")
            .SetEnvironmentVariable("CI", "true")
            .SetEnvironmentVariable("NODE_OPTIONS", "--max-old-space-size=4096"));

        Assert.Equal("true", plan.Environment["CI"]);
        Assert.Equal("--max-old-space-size=4096", plan.Environment["NODE_OPTIONS"]);
    }

    // ---- Object-init parity ----

    [Fact]
    public void ObjectInit_Produces_Identical_Args_To_Fluent()
    {
        var binary = FakeBinary();
        var fluent = Eslint.Scan(s => s
            .SetBinary(binary)
            .AddTarget("src")
            .AddTarget("tests")
            .AddConfig("eslint.config.js")
            .SetSarif()
            .SetOutputFile("out.sarif")
            .SetMaxWarnings(0)
            .SetQuiet());

        var settings = new EslintScanSettings
        {
            Binary = binary,
            Sarif = true,
            OutputFile = "out.sarif",
            MaxWarnings = 0,
            Quiet = true,
        };
        settings.Targets.Add("src");
        settings.Targets.Add("tests");
        settings.Configs.Add("eslint.config.js");
        var objInit = Eslint.Scan(settings);

        Assert.Equal(fluent.Arguments, objInit.Arguments);
        Assert.Equal(fluent.Executable, objInit.Executable);
    }

    // ---- Realistic invocation ----

    [Fact]
    public void Realistic_Security_Pipeline_Invocation_Shape()
    {
        var binary = new EslintBinaryResolution
        {
            Executable = "/work/web/node_modules/.bin/eslint",
            Source = EslintResolutionSource.ProjectLocal,
        };

        var plan = Eslint.Scan(s => s
            .SetBinary(binary)
            .SetWorkingDirectory("/work/web")
            .AddTarget("src")
            .AddTarget("tests")
            .AddConfig("eslint.config.js")
            .SetSarif()
            .SetOutputFile("/work/artifacts/security/eslint.sarif")
            .SetMaxWarnings(0)
            .SetQuiet());

        Assert.Equal("/work/web/node_modules/.bin/eslint", plan.Executable);
        Assert.Equal("/work/web", plan.WorkingDirectory);

        // --config eslint.config.js comes first
        var configIdx = IndexOf(plan.Arguments, "--config");
        Assert.True(configIdx >= 0);
        Assert.Equal("eslint.config.js", plan.Arguments[configIdx + 1]);

        // SARIF wired
        Assert.Contains("--format", plan.Arguments);
        Assert.Contains("@microsoft/eslint-formatter-sarif", plan.Arguments);
        Assert.Contains("--output-file", plan.Arguments);
        Assert.Contains("/work/artifacts/security/eslint.sarif", plan.Arguments);

        // Behavior flags
        Assert.Contains("--max-warnings", plan.Arguments);
        Assert.Contains("0", plan.Arguments);
        Assert.Contains("--quiet", plan.Arguments);

        // Targets at the end
        Assert.Equal("src", plan.Arguments[^2]);
        Assert.Equal("tests", plan.Arguments[^1]);
    }

    // ---- Boundary fuzz ----

    [Theory]
    [InlineData("path with spaces/src")]
    [InlineData("packages/Δ-π/src")]
    [InlineData("packages/sub'project/src")]
    public void Target_Path_Roundtrips_Verbatim(string path)
    {
        var plan = Eslint.Scan(s => s.SetBinary(FakeBinary()).AddTarget(path));
        Assert.Equal(path, plan.Arguments[^1]);
    }

    [Fact]
    public void Bulk_Configs_All_Emit()
    {
        var faker = new Faker();
        var paths = Enumerable.Range(0, 25)
            .Select(_ => $"configs/{faker.Hacker.Noun()}-{faker.Random.AlphaNumeric(5)}.js")
            .Distinct()
            .ToList();

        var plan = Eslint.Scan(s =>
        {
            s.SetBinary(FakeBinary()).AddTarget("src");
            foreach (var p in paths) s.AddConfig(p);
        });

        Assert.Equal(paths.Count, plan.Arguments.Count(a => a == "--config"));
        foreach (var p in paths) Assert.Contains(p, plan.Arguments);
    }
}
