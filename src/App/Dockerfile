# 用于执行 LLM 编写的 pptxgenjs 脚本的最小化沙箱镜像。
#
# 该镜像是 DockerSkillScriptRunner 与 Computer-Use 工具（run_in_container）的执行后端。
# .NET 宿主留在本机；只有不可信的、LLM 编写的 JavaScript 在这里运行，与宿主隔离。

FROM node:20-slim

RUN npm install -g pptxgenjs@4.0.1 \
    && npm cache clean --force
ENV NODE_PATH=/usr/local/lib/node_modules

WORKDIR /work

USER node

CMD ["node", "--version"]