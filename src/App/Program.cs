// Copyright (c) ShuaiHua Du. All rights reserved.

using System.Diagnostics;
using System.Text;
using Azure.AI.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ScriptExecutor;

Console.OutputEncoding = Encoding.UTF8;

string endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
string deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME")
    ?? throw new InvalidOperationException("AZURE_OPENAI_DEPLOYMENT_NAME is not set.");
string apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY")
    ?? throw new InvalidOperationException("AZURE_OPENAI_API_KEY is not set.");


const string ImageName = "skill-sandbox:latest";
string skillPath = Path.Combine(AppContext.BaseDirectory, "skills");
string workDirectory = Path.Combine(AppContext.BaseDirectory, "sandbox-work");

Directory.CreateDirectory(workDirectory);
foreach (string entry in Directory.EnumerateFileSystemEntries(workDirectory))
{
    try
    {
        if (File.Exists(entry))
        {
            File.Delete(entry);
        }
        else
        {
            Directory.Delete(entry, recursive: true);
        }
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
}

// 构建沙箱镜像
await EnsureDockerImageAsync(ImageName, Path.Combine(AppContext.BaseDirectory, "Dockerfile"));

var dockerExecutor = new DockerExecutor(ImageName, workDirectory, skillPath);
var dockerRunner = new DockerSkillScriptRunner(dockerExecutor);
var computerUseTools = new ComputerUseTools(dockerExecutor);

var skillsProvider = new AgentSkillsProvider(
    skillPath: skillPath,
    dockerRunner.RunAsync);


IChatClient chatClient = new AzureOpenAIClient(new Uri(endpoint), new System.ClientModel.ApiKeyCredential(apiKey))
    .GetChatClient(deploymentName)
    .AsIChatClient();

AIAgent agent = chatClient
    .AsBuilder()
    .UseFunctionInvocation()
    .BuildAIAgent(new ChatClientAgentOptions
    {
        Name = "SkillsAgent",
        ChatOptions = new()
        {
            Instructions =
                "你是一个 PowerPoint（PPTX）幻灯片制作助手，使用中文工作。" +
                "你拥有一个隔离的 Node.js 沙箱容器（已预装 pptxgenjs）和一组文件/执行工具，" +
                "请按以下“边写代码边执行”的方式完成制作：" +
                "(1) 调用一次 `load_skill`，skillName='pptx'，了解技能说明；" +
                "(2) 调用 `read_skill_resource`，resourceName='pptxgenjs.md'，" +
                "仔细阅读 pptxgenjs 的 API 用法与避坑指南（颜色不要带 '#'、用 bullet:true、option 对象不要复用等）；" +
                "(3) 调用 `write_file`，path='agent.js'，content 为你编写的完整 Node.js 脚本：" +
                "用 `const pptxgen = require(\"pptxgenjs\");` 创建演示文稿，逐页 addText/addShape，" +
                "中文请设置 fontFace 为 'Microsoft YaHei'，最后用 `pres.writeFile({ fileName: \"xxxxx.pptx\" })` 输出到当前目录；" +
                "(4) 调用 `run_in_container`，command='node agent.js' 执行脚本生成 .pptx；" +
                "(5) 若执行报错，读取错误信息，用 `write_file` 修正 agent.js 后重新执行，直到成功。" +
                "最终请确认生成了 xxxxx.pptx。所有代码都在容器中运行，不要在回答里直接粘贴大段 XML。",
            Tools = computerUseTools.CreateTools()
        },
        AIContextProviders = [skillsProvider]
    });


Console.WriteLine("请输入需要制作的 PPT 主题（默认：Agent Skill）：");

string? pptSubject = Console.ReadLine();

if (string.IsNullOrWhiteSpace(pptSubject))
{
    pptSubject = "Agent Skill";
}

Console.WriteLine("用 Docker 沙箱 + Computer-Use 生成 PPT - {0}", pptSubject);
Console.WriteLine(new string('-', 60));


Console.WriteLine();
Console.WriteLine("--- Response trace (streaming) ---");


var finalText = new StringBuilder();
bool lastWasText = false;


await foreach (AgentResponseUpdate update in agent.RunStreamingAsync($"请帮我制作一套关于{pptSubject}的PPT").ConfigureAwait(false))
{
    // 一个增量更新里可能包含多块内容，逐块按类型处理。
    foreach (AIContent content in update.Contents)
    {
        switch (content)
        {
            // 模型发起的工具调用（如 write_file / run_in_container）。
            case FunctionCallContent call:
                if (lastWasText) { Console.WriteLine(); lastWasText = false; }
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[{update.Role}] FunctionCall: {call.Name}({System.Text.Json.JsonSerializer.Serialize(call.Arguments)})");
                Console.ResetColor();
                break;
            // 工具执行后的返回结果。
            case FunctionResultContent result:
                if (lastWasText) { Console.WriteLine(); lastWasText = false; }
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[{update.Role}] FunctionResult({result.CallId}): {result.Result}");
                Console.ResetColor();
                break;
            // 模型生成的文本增量：逐段输出（不换行），并累积到最终文本。
            case TextContent text when !string.IsNullOrEmpty(text.Text):
                Console.Write(text.Text);
                finalText.Append(text.Text);
                lastWasText = true;
                break;
        }
    }
}

if (lastWasText)
{
    Console.WriteLine();
}
Console.WriteLine("----------------------");
Console.WriteLine();

Console.WriteLine($"Agent: {finalText}");

// --- 报告在容器内生成的产物 ---
// pptxgenjs 直接把 .pptx 写入 bind mount 的工作目录，所以它已经在宿主上了。
// 这里只是找出它并报告路径。
Console.WriteLine();
string[] producedFiles = Directory.Exists(workDirectory)
    ? Directory.GetFiles(workDirectory, "*.pptx", SearchOption.AllDirectories)
    : [];

if (producedFiles.Length > 0)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"已在沙箱容器中生成 {producedFiles.Length} 个 PPT 文件，保存在宿主目录：");
    foreach (string file in producedFiles)
    {
        var info = new FileInfo(file);
        Console.WriteLine($"  {info.FullName}  ({info.Length:N0} bytes)");
    }
    Console.ResetColor();
}
else
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"未在工作目录中找到 .pptx 文件：{workDirectory}");
    Console.WriteLine("请检查模型是否调用了 write_file + run_in_container，并查看上方的容器输出。");
    Console.ResetColor();
}

static async Task EnsureDockerImageAsync(string imageName, string dockerfilePath)
{
    var inspect = Process.Start(new ProcessStartInfo
    {
        FileName = "docker",
        ArgumentList = { "image", "inspect", imageName },
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    });

    if (inspect is not null)
    {
        await inspect.WaitForExitAsync().ConfigureAwait(false);
        if (inspect.ExitCode == 0)
        {
            return;
        }
    }

    string contextDir = Path.GetDirectoryName(dockerfilePath) ?? ".";
    Console.WriteLine($"正在构建 Docker 镜像 '{imageName}'（首次运行，需要片刻）……");

    var build = Process.Start(new ProcessStartInfo
    {
        FileName = "docker",
        ArgumentList = { "build", "-t", imageName, "-f", dockerfilePath, contextDir },
        UseShellExecute = false,
        CreateNoWindow = true,
    }) ?? throw new InvalidOperationException("无法启动 'docker build'，请确认已安装 Docker 并在 PATH 中。");

    await build.WaitForExitAsync().ConfigureAwait(false);
    if (build.ExitCode != 0)
    {
        throw new InvalidOperationException($"Docker 镜像构建失败（退出码 {build.ExitCode}）。");
    }
}
