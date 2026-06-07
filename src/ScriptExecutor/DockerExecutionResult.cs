// Copyright (c) ShuaiHua Du. All rights reserved.

using System.Text;

namespace ScriptExecutor;

/// <summary>
/// 容器的执行结果
/// </summary>
/// <param name="ExitCode">退出代码</param>
/// <param name="StdOut">标准输出</param>
/// <param name="StdErr">标准错误</param>
public sealed record DockerExecutionResult(int ExitCode, string StdOut, string StdErr)
{
    public bool IsSuccess => ExitCode == 0;

    public string ToToolResult()
    {
        StringBuilder sb = new();

        if (!string.IsNullOrEmpty(StdOut))
        {
            sb.Append(StdOut);
        }

        if (!string.IsNullOrEmpty(StdErr))
        {
            if (sb.Length > 0)
            {
                sb.AppendLine();
            }
            sb.Append(StdErr).AppendLine(StdErr);
        }


        if (ExitCode != 0)
        {
            if (sb.Length > 0)
            {
                sb.AppendLine();
            }

            sb.Append($"Exit with code: {ExitCode}.");
        }

        return sb.Length == 0 ? "(docker executor no output)" : sb.ToString().Trim();
    }
}
