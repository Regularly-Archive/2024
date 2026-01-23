# Code Runner Backend

基于 Python (FastAPI) 和 Docker 构建的多语言代码执行平台。在隔离的容器中运行代码片段和项目。

## 功能特性

- **多语言支持**: Python、JavaScript、TypeScript、C#、Go、C/C++、Java、Rust、Lua、Bash、Jupyter
- **沙箱执行**: 所有代码在 Docker 容器中运行，确保安全
- **项目支持**: 上传并运行多文件项目压缩包
- **产物收集**: 收集生成的文件（CSV、PDF、图片等）
- **依赖管理**: 自动安装支持语言的依赖

## 快速开始

```bash
# 安装依赖
pip install -r requirements.txt

# 构建 Docker 镜像
cd docker
./build-images.sh  # Linux/macOS
./build-images.ps1 # Windows

# 启动服务
python server.py
```

服务运行在 `http://localhost:8001`

## API 端点

### 运行代码片段

```bash
curl -X POST http://localhost:8001/api/code/run \
  -H "Content-Type: application/json" \
  -d '{
    "code": "print(\"Hello, World!\")",
    "language": "python3"
  }'
```

### 运行项目压缩包

```bash
curl -X POST http://localhost:8001/api/project/run-archive \
  -F "archive_file=@project.zip"
```

### 运行 Bash 脚本

```bash
curl -X POST http://localhost:8001/api/project/run-bash \
  -F "archive_file=@script.zip" \
  -F "main_script=run.sh"
```

### 获取产物

```bash
curl http://localhost:8001/api/projects/{project_id}/executions/{execution_id}/artifacts
```

## 支持的语言

| 语言 | 镜像 | 入口文件 |
|------|------|----------|
| Python 3 | `code_runner/python3` | `main.py`, `app.py` |
| JavaScript | `code_runner/nodejs` | `index.js`, `app.js` |
| TypeScript | `code_runner/nodejs` | `index.ts`, `app.ts` |
| C# (.NET) | `code_runner/dotnet` | `Program.cs` |
| Go | `code_runner/go` | `main.go` |
| C/C++ | `code_runner/cpp` | `main.cpp`, `Makefile` |
| Java | `code_runner/java` | `Main.java`, `pom.xml` |
| Rust | `code_runner/rust` | `src/main.rs` |
| Lua | `code_runner/lua` | `main.lua` |
| Bash | `code_runner/bash` | `*.sh` |
| Jupyter | `code_runner/jupyterlab` | `*.ipynb` |

## 项目结构

```
backend/
├── server.py           # FastAPI 入口
├── models.py           # 内部业务模型
├── views.py            # API 请求/响应模型
├── config.py           # 语言运行时配置
├── utils.py            # 工具函数
├── handlers/           # 语言处理器
│   ├── baseHandler.py  # 抽象基类
│   ├── resolver.py     # 处理器解析器
│   └── python/, java/, go/, ...
├── services/           # 核心服务
│   ├── runner.py       # 代码执行服务
│   ├── docker.py       # Docker 客户端
│   ├── collector.py    # 产物收集器
│   └── detector.py     # 项目检测器
├── docker/             # Docker 镜像
└── tests/              # 测试文件
```

## 配置

### 环境变量

| 变量 | 默认值 | 说明 |
|------|--------|------|
| `server_port` | `8001` | 服务端口 |
| `MINIMAX_API_KEY` | - | LLM 服务 API 密钥 |

### 产物类型

编辑 `config.py` 中的 `ALLOWED_ARTIFACT_PATTERNS` 自定义可收集的产物类型。

## 开发

### 添加新语言支持

1. **更新 `config.py`**: 添加语言运行时配置
2. **创建 Docker 镜像**: 在 `docker/{language}/` 目录下添加 Dockerfile
3. **创建处理器**: 在 `handlers/{language}/` 下实现 `BaseHandler` 子类
4. **更新 `HandlerResolver`**: 注册新的处理器

### 运行测试

```bash
# 测试项目压缩包支持
python tests/project_tester.py

# 测试 Bash 脚本执行
python tests/bash_tester.py

# 测试 Jupyter notebook 执行
python tests/jupyter_tester.py
```

## 许可证

MIT
