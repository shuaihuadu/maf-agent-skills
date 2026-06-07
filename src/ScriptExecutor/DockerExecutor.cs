// Copyright (c) ShuaiHua Du. All rights reserved.

using System.Diagnostics;
using System.Text;

namespace ScriptExecutor;

/// <summary>
/// 使用Docker执行Skill的脚本
/// </summary>
public sealed class DockerExecutor
{
    private readonly string _imageName;
    private readonly string _hostWorkDirectory;
    private readonly string? _hostSkillDirectory;
    private readonly TimeSpan _timeout;

    public DockerExecutor(string imageName, string hostWorkDirectory, string? hostSkillDirectory = null, TimeSpan? timeout = null)
    {
        _imageName = imageName;
        _hostWorkDirectory = Path.GetFullPath(hostWorkDirectory);

        Directory.CreateDirectory(_hostWorkDirectory);

        _hostSkillDirectory = hostSkillDirectory is null ? null : Path.GetFullPath(hostSkillDirectory);
        _timeout = timeout ?? TimeSpan.FromMinutes(5);
    }

    public string HostWorkDirectory => _hostWorkDirectory;

    public string? HostSkillDirectory => _hostSkillDirectory;

    /// <summary>
    /// 在一个全新、隔离的容器中运行命令，并返回捕获到的输出
    /// 注意：确保执行的宿主机安装了 Docker
    /// </summary>
    /// <param name="commandArgs"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<DockerExecutionResult> RunAsync(IReadOnlyList<string> commandArgs, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--rm");
        startInfo.ArgumentList.Add("--network");
        startInfo.ArgumentList.Add("none");
        startInfo.ArgumentList.Add("--memory");
        startInfo.ArgumentList.Add("512m");
        startInfo.ArgumentList.Add("--pids-limit");
        startInfo.ArgumentList.Add("256");
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add($"{_hostWorkDirectory}:/work");

        if (_hostSkillDirectory is not null)
        {
            startInfo.ArgumentList.Add("-v");
            startInfo.ArgumentList.Add($"{_hostSkillDirectory}:/skill:ro");
        }
        startInfo.ArgumentList.Add("-w");
        startInfo.ArgumentList.Add("/work");
        startInfo.ArgumentList.Add(_imageName);
        foreach (string arg in commandArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        Process? process = null;
        try
        {
            process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("启动 'docker' 进程失败。请确认已安装 Docker 且在 PATH 中。");

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);

            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

            string stdout = await outputTask.ConfigureAwait(false);
            string stderr = await errorTask.ConfigureAwait(false);

            return new DockerExecutionResult(process.ExitCode, stdout.TrimEnd(), stderr.TrimEnd());
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            process?.Kill(entireProcessTree: true);
            return new DockerExecutionResult(-1, string.Empty, $"容器运行超时，已超过 {_timeout.TotalSeconds:N0} 秒。");
        }
        catch (OperationCanceledException)
        {
            process?.Kill(entireProcessTree: true);
            throw;
        }
        finally
        {
            process?.Dispose();
        }
    }
}
