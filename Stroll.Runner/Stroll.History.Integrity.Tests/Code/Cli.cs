using System.Diagnostics;
using System.Text.Json;

namespace Stroll.Runner.HistoryIntegrity;

public sealed class Cli
{
    private readonly TestConfig _cfg;
    private readonly string _workdir;

    public Cli(TestConfig cfg, string workdir)
    {
        _cfg = cfg;
        _workdir = workdir;
    }

    public async Task<JsonDocument> RunAsync(params string[] args)
    {
        using var cts = new CancellationTokenSource(_cfg.TimeoutMs);
        var psi = BuildStartInfo(args);
        var p = new Process { StartInfo = psi };
        p.Start();
        using var reg = cts.Token.Register(() => { try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch {} });
        var stdout = await p.StandardOutput.ReadToEndAsync();
        var stderr = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();

        if (p.ExitCode != 0)
            throw new Exception($"stroll.history failed ({p.ExitCode})\nCMD: {psi.FileName} {psi.Arguments}\nSTDERR:\n{stderr}");

        try { return JsonDocument.Parse(stdout); }
        catch (Exception e) { throw new Exception($"Invalid JSON from stroll.history.\nSTDOUT:\n{stdout}\n", e); }
    }

    private ProcessStartInfo BuildStartInfo(string[] args)
    {
        if (_cfg.Mode.Equals("exe", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(_cfg.Exe)) throw new InvalidOperationException("exe mode requires 'exe' path");
            var psi = new ProcessStartInfo(_cfg.Exe)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = _workdir
            };
            foreach (var kv in _cfg.Env) psi.Environment[kv.Key] = kv.Value;
            foreach (var a in args) psi.ArgumentList.Add(a);
            return psi;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(_cfg.Project)) throw new InvalidOperationException("dotnet-run mode requires 'project' path");
            var psi = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = _workdir
            };
            foreach (var kv in _cfg.Env) psi.Environment[kv.Key] = kv.Value;
            psi.ArgumentList.Add("run");
            psi.ArgumentList.Add("--project"); psi.ArgumentList.Add(_cfg.Project!);
            psi.ArgumentList.Add("--");
            foreach (var a in args) psi.ArgumentList.Add(a);
            return psi;
        }
    }
}
