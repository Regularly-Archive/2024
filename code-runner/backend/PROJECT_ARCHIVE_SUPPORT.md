# 压缩包项目支持文档

## 支持的语言

 `/api/project/run-archive` 端点现已支持以下语言的项目压缩包上传和运行：

### 已支持语言

1. **Python** ✅
   - 通过 `requirements.txt`、`main.py` 或 `.py` 文件检测
   - 自动安装pip依赖
   - 支持单文件和多文件项目

2. **JavaScript** ✅
   - 通过 `package.json`、`.js` 文件检测
   - 自动运行 `npm install`
   - 支持npm依赖

3. **C#** ✅
   - 通过 `Program.cs`、`.csproj`、`.cs` 文件检测
   - 支持 dotnet 控制台应用
   - 支持 .csx 脚本文件
   - 使用 dotnet 命令运行

4. **Go** ✅
   - 通过 `go.mod`、`main.go` 文件检测
   - 自动运行 `go mod tidy`
   - 支持Go模块

5. **C/C++** ✅
   - 通过 `main.cpp`、`main.c`、`Makefile` 检测
   - 自动编译：`g++ main.cpp -o main`
   - 支持多文件编译

6. **Java** ✅ (V1)
   - 通过 `pom.xml`、`.java` 文件检测
   - 支持Maven项目
   - 单文件Java支持jbang

### 项目类型检测

系统会根据以下优先级自动检测项目类型：

1. **特定配置文件**：`package.json`、`requirements.txt`、`go.mod` 等
2. **扩展名匹配**：`.cs`、`.go`、`.cpp` 等
3. **默认入口文件**：`main.py`、`index.js` 等

### 使用示例

#### Python 项目
```bash
# 测试Python项目上传
curl -X POST http://localhost:8001/api/project/run-archive \
  -F "archive_file=@python_project.zip" \
  -F "language=python3"
```

#### C++ 项目
```bash
# 测试C++项目上传
curl -X POST http://localhost:8001/api/project/run-archive \
  -F "archive_file=@cpp_project.zip" \
  -F "language=cpp" \
  -F "entry_point=main.cpp"
```

#### Go 项目
```bash
# 测试Go项目上传
curl -X POST http://localhost:8001/api/project/run-archive \
  -F "archive_file=@go_project.zip" \
  # 自动检测，无需指定语言
```

### 测试脚本

运行测试脚本验证多语言支持：

```bash
python test_mixed_lang_projects.py
```

### Docker 镜像

确保以下 Docker 镜像已构建：

```bash
cd docker
# Linux/macOS
./build-images.sh

# Windows PowerShell
./build-images.ps1
```

已构建的镜像：
- `code_runner/python3` ✅
- `code_runner/nodejs` ✅
- `code_runner/go` ✅
- `code_runner/cpp` ✅
- `code_runner/dotnet` ✅
- `code_runner/java` ✅

### 添加新语言支持

要添加新语言支持，需要：

1. **更新 `config.py`**
   - 添加 `PROJECT_DETECTORS` 规则
   - 添加 `LANGUAGE_CONFIG` 配置

2. **创建 Docker 镜像**
   - 在 `docker/` 目录下创建新语言目录
   - 添加 Dockerfile
   - 构建镜像

3. **测试**
   - 创建示例项目压缩包
   - 运行测试脚本验证

### 限制和注意事项

1. **C# 限制**：目前主要支持控制台应用程序，不支持 GUI 项目
2. **C++ 限制**：需要标准的 makefile 或单个 main.cpp 文件
3. **Go 限制**：需要有效的 go.mod 文件
4. **项目大小**：默认最大 50MB（可在 API 中调整）
5. **安全限制**：所有代码在 Docker 沙箱中运行

### 错误处理

- **语言检测失败**：返回错误和建议的语言列表
- **入口点未找到**：列出所有检测到的文件让用户指定
- **编译/构建失败**：返回详细的错误信息和输出
- **依赖安装失败**：提供错误详情

该功能使代码运行器从简单的脚本执行工具升级为全功能的多语言项目运行平台。支持复杂的真实项目结构和构建流程。