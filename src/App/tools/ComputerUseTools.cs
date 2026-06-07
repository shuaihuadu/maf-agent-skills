// Copyright (c) ShuaiHua Du. All rights reserved.

// 填补技能机制中“编写 + 执行”能力缺口的 Computer-Use 工具
// 这种“纯 Computer-Use”工作流，本类暴露一小组自定义 AIFunction 工具：
//
//   * write_file       —— 在工作目录中写入一个文件
//   * read_file        —— 读取一个文件
//   * list_files       —— 列出工作目录
//   * run_in_container —— 在沙箱中执行命令

using System.ComponentModel;
using Microsoft.Extensions.AI;
using ScriptExecutor;

internal sealed class ComputerUseTools
{
    private readonly DockerExecutor _executor;
    private readonly string _workDirectory;

    public ComputerUseTools(DockerExecutor executor)
    {
        _executor = executor;
        _workDirectory = executor.HostWorkDirectory;
    }

    public IList<AITool> CreateTools() =>
    [
        AIFunctionFactory.Create(WriteFile),
        AIFunctionFactory.Create(ReadFile),
        AIFunctionFactory.Create(ListFiles),
        AIFunctionFactory.Create(RunInContainer),
    ];

    [Description("Create or overwrite a text file in the working directory. Use this to write the JavaScript (e.g. agent.js) that builds the presentation with pptxgenjs.")]
    private string WriteFile(
        [Description("Relative file name within the working directory, e.g. 'agent.js'. Subdirectories are allowed.")] string path,
        [Description("The full text content to write to the file.")] string content)
    {
        if (!TryResolve(path, out string fullPath, out string? error))
        {
            return error!;
        }

        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
        return $"已向 {path} 写入 {content.Length} 个字符。";
    }

    [Description("Read a text file from the working directory.")]
    private string ReadFile(
        [Description("Relative file name within the working directory.")] string path)
    {
        if (!TryResolve(path, out string fullPath, out string? error))
        {
            return error!;
        }
        if (!File.Exists(fullPath))
        {
            return $"错误：未找到文件：{path}";
        }
        return File.ReadAllText(fullPath);
    }

    [Description("List the files currently in the working directory.")]
    private string ListFiles()
    {
        var entries = Directory.EnumerateFileSystemEntries(_workDirectory)
            .Select(p => Path.GetRelativePath(_workDirectory, p).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
        return entries.Count == 0 ? "（工作目录为空）" : string.Join("\n", entries);
    }

    [Description("Run a command inside the isolated Node sandbox container (working directory is /work). For example, run 'node agent.js' to execute the script you wrote, which generates the .pptx file. pptxgenjs is preinstalled.")]
    private async Task<string> RunInContainer(
        [Description("The command to run, e.g. 'node agent.js'. It is executed inside the container's /work directory.")] string command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return "错误：命令不能为空。";
        }

        IReadOnlyList<string> argv = SplitCommandLine(command);
        if (argv.Count == 0)
        {
            return "错误：命令不能为空。";
        }

        DockerExecutionResult result = await _executor.RunAsync(argv, cancellationToken).ConfigureAwait(false);
        return result.ToToolResult();
    }

    private bool TryResolve(string path, out string fullPath, out string? error)
    {
        fullPath = Path.GetFullPath(Path.Combine(_workDirectory, path));

        string root = _workDirectory.EndsWith(Path.DirectorySeparatorChar)
            ? _workDirectory
            : _workDirectory + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            error = $"错误：'{path}' 解析后落在工作目录之外，不允许访问。";
            return false;
        }

        error = null;
        return true;
    }

    private static IReadOnlyList<string> SplitCommandLine(string command)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        foreach (char c in command)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }
}
