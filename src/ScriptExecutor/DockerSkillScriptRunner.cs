// Copyright (c) ShuaiHua Du. All rights reserved.

using System.Text.Json;
using Microsoft.Agents.AI;

namespace ScriptExecutor;

public sealed class DockerSkillScriptRunner
{
    private readonly DockerExecutor _executor;

    public DockerSkillScriptRunner(DockerExecutor dockerExecutor)
    {
        _executor = dockerExecutor;
    }

    public async Task<object?> RunAsync(AgentFileSkill skill, AgentFileSkillScript script, JsonElement? arguments, IServiceProvider? serviceProvider, CancellationToken cancellationToken = default)
    {
        if (_executor.HostSkillDirectory is null)
        {
            return "错误：DockerExecutor 创建时未提供技能目录，因此无法在容器内定位技能脚本。";
        }

        if (!File.Exists(script.FullPath))
        {
            return $"错误：未找到脚本文件：{script.FullPath}";
        }

        // 把宿主脚本路径映射成容器内的只读 /skill/... 路径

        string relative = Path.GetRelativePath(_executor.HostSkillDirectory, script.FullPath)
            .Replace('\\', '/');

        if (relative.StartsWith("..", StringComparison.Ordinal))
        {
            return $"错误：脚本 '{script.Name}' 在已挂载的技能目录之外，无法执行。";
        }

        string containerScriptPath = "/skill/" + relative;
        string extension = Path.GetExtension(script.FullPath);

        string? interpreter = extension.ToLowerInvariant() switch
        {
            ".py" => "python3",
            ".js" => "node",
            ".sh" => "bash",
            _ => null,
        };

        if (interpreter is null)
        {
            return $"错误：DockerSkillScriptRunner 在最小镜像中不支持 '{extension}' 脚本（支持：.py 用 python、.js 用 node、.sh 用 bash）。";
        }

        var command = new List<string> { interpreter, containerScriptPath };

        if (arguments is { ValueKind: JsonValueKind.Array } jsonArray)
        {
            foreach (JsonElement element in jsonArray.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                {
                    return $"错误：文件版技能脚本只接受字符串类型的 CLI 参数，但收到了类型为 '{element.ValueKind}' 的 JSON 元素。";
                }
                command.Add(element.GetString()!);
            }
        }
        else if (arguments is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined })
        {
            return $"错误：预期收到一个由 CLI 参数组成的 JSON 数组，但收到的是 '{arguments.Value.ValueKind}'。";
        }

        DockerExecutionResult result = await _executor.RunAsync(command, cancellationToken).ConfigureAwait(false);

        return result.ToToolResult();
    }
}
