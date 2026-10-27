using L = DockNest.Services.LocalizationService;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DockNest.Services;

public sealed record GitResult(int ExitCode, string Output)
{
    public bool Success => ExitCode == 0;
}

public sealed class GitService(string executable = "git")
{
    public async Task<string?> FindRepositoryAsync(string target, CancellationToken cancellationToken = default)
    {
        // Let the cancellable Git process access network paths, rather than blocking on Directory.Exists.
        var result = await RunAsync(target, ["rev-parse", "--show-toplevel"], cancellationToken);
        if (result.Success) return result.Output.Trim();
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(target));
        if (string.IsNullOrEmpty(parent)) return null;
        result = await RunAsync(parent, ["rev-parse", "--show-toplevel"], cancellationToken);
        return result.Success ? result.Output.Trim() : null;
    }

    public async Task<GitResult> RunAsync(string repository, IEnumerable<string> arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_PAGER"] = "cat";
        start.Environment["GCM_INTERACTIVE"] = "Never";
        start.ArgumentList.Add("--no-pager");
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(repository);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        process.Start();
        var output = ReadOutputAsync(process.StandardOutput);
        var errors = ReadOutputAsync(process.StandardError);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, errors);
            if (cancellationToken.IsCancellationRequested) throw;
            throw new TimeoutException(L.Get("GitTimeout"));
        }
        var standardOutput = await output;
        var standardError = await errors;
        var combined = standardOutput + (standardError.Length > 0 ? "\n" + standardError : "");
        return new GitResult(process.ExitCode, combined.Trim());
    }

    private static async Task<string> ReadOutputAsync(StreamReader reader)
    {
        const int maximumCharacters = 1_000_000;
        var text = new StringBuilder();
        var buffer = new char[4096];
        var truncated = false;
        int count;
        while ((count = await reader.ReadAsync(buffer)) > 0)
        {
            var keep = Math.Min(count, maximumCharacters - text.Length);
            text.Append(buffer, 0, keep);
            truncated |= keep < count;
        }
        if (truncated) text.Append("\n" + L.Get("GitTruncated"));
        return text.ToString();
    }
}
