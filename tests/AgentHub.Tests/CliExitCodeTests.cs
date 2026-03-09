using System.Diagnostics;
using Xunit;

namespace AgentHub.Tests;

/// <summary>
/// Smoke tests verifying CLI exit code mapping:
/// 0 = success, 1 = HttpRequestException (API error), 2 = OperationCanceledException (timeout).
/// These test the behavior documented in CLI-02 requirement.
/// </summary>
public class CliExitCodeTests
{
    private static readonly string s_projectPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "AgentHub.Cli", "AgentHub.Cli.csproj"));

    /// <summary>
    /// Verifies that a command against an unreachable server returns exit code 1
    /// (HttpRequestException mapped to exit code 1).
    /// </summary>
    [Fact]
    public async Task CliReturnsExitCode1_WhenApiServerUnreachable()
    {
        var (exitCode, stdout, stderr) = await RunCliAsync(
            "--server", "http://localhost:59999", "session", "list", "--json");

        Assert.Equal(1, exitCode);
        // JSON error output goes to stderr for JsonFormatter.WriteError
        var combinedOutput = stdout + stderr;
        Assert.Contains("error", combinedOutput, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that --help returns exit code 0 (success path).
    /// </summary>
    [Fact]
    public async Task CliReturnsExitCode0_OnSuccessfulCommand()
    {
        var (exitCode, stdout, _) = await RunCliAsync("--help");

        Assert.Equal(0, exitCode);
        Assert.Contains("AgentHub CLI", stdout);
    }

    /// <summary>
    /// Verifies that config show (no network needed) returns exit code 0.
    /// </summary>
    [Fact]
    public async Task CliReturnsExitCode0_ForConfigShow()
    {
        var (exitCode, stdout, _) = await RunCliAsync("config", "show", "--json");

        Assert.Equal(0, exitCode);
        Assert.Contains("serverUrl", stdout, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunCliAsync(
        params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            ArgumentList = { "run", "--project", s_projectPath, "--no-build", "--" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return (process.ExitCode, stdout, stderr);
    }
}
