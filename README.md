# MAF Agent Skills — Docker 沙箱 + Computer-Use 生成 PPT

> 基于 Microsoft Agent Framework（MAF）.NET `1.9.0`。Agent Skills 模块当前标记为 `[Experimental]`（诊断 ID `MAAI001`），API 可能在后续版本变更。

本示例演示如何让 Agent **自己编写 Node.js 代码并在隔离的 Docker 容器中执行**，用 Anthropic 官方 `pptx` 技能里的 `pptxgenjs` 教程从零生成一份 PowerPoint 演示文稿（`.pptx`）。

这是一个"纯 Computer-Use"工作流：Agent 读完技能教程后**现写 `agent.js`、跑、看报错、自我修正**，全部代码都在断网、限资源、非 root 的一次性容器里运行，生成的 `.pptx` 通过卷映射自动落回宿主。

---

## 它解决了什么问题

MAF 原生的三个技能工具（`load_skill` / `read_skill_resource` / `run_skill_script`）中，`run_skill_script` **只能运行技能里已存在的脚本**。但官方 `pptx` 技能的"从零创建"能力写在 `pptxgenjs.md` 教程里——它教 Agent 怎么**写一段新的 Node.js 代码**，而不是提供一个现成脚本。

本示例用两块拼图补齐这个缺口：

- **`DockerSkillScriptRunner`** —— 第三个脚本执行器（与 Hyperlight、Subprocess 并列），把脚本放进 Docker 容器执行；
- **`ComputerUseTools`** —— 补齐 `write_file` / `read_file` / `list_files` / `run_in_container` 四个工具，让 Agent 能"写文件 + 执行"；
- 二者共用 **`DockerExecutor`** 作为容器执行后端。

应用本身不进容器，只有 Agent 写的不可信代码进入断网、限资源、非 root 的一次性容器；生成的 `.pptx` 通过卷映射自动落回宿主。

---

## 如何运行

**前置要求**：.NET 10 SDK、Docker（Linux 容器，daemon 运行中）、一个支持函数调用的 Azure OpenAI 部署。无需本机安装 Node.js / pptxgenjs（已打包进镜像）。

设置环境变量并运行：

```powershell
$env:AZURE_OPENAI_ENDPOINT = "https://your-resource.openai.azure.com/"
$env:AZURE_OPENAI_DEPLOYMENT_NAME = "gpt-4o"
$env:AZURE_OPENAI_API_KEY = "your-api-key"
```

首次运行会自动构建沙箱镜像（之后跳过）。按提示输入主题（直接回车用默认"Agent Skill"），生成PPT。

---

## 相关链接

Microsoft Agent Framework 官方项目地址：https://github.com/microsoft/agent-framework
