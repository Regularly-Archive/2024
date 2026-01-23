# Code Runner Backend

A multi-language code execution platform built with Python (FastAPI) and Docker. Execute code snippets and projects in isolated containers.

## Features

- **Multi-language Support**: Python, JavaScript, TypeScript, C#, Go, C/C++, Java, Rust, Lua, Bash, Jupyter
- **Sandboxed Execution**: All code runs in Docker containers for security
- **Project Support**: Upload and run multi-file projects as archives
- **Artifact Collection**: Collect generated files (CSV, PDF, images, etc.)
- **Dependency Management**: Automatic dependency installation for supported languages

## Quick Start

```bash
# Install dependencies
pip install -r requirements.txt

# Build Docker images
cd docker
./build-images.sh  # Linux/macOS
./build-images.ps1 # Windows

# Start the server
python server.py
```

The service runs at `http://localhost:8001`

## API Endpoints

### Run Code Snippet

```bash
curl -X POST http://localhost:8001/api/code/run \
  -H "Content-Type: application/json" \
  -d '{
    "code": "print(\"Hello, World!\")",
    "language": "python3"
  }'
```

### Run Project Archive

```bash
curl -X POST http://localhost:8001/api/project/run-archive \
  -F "archive_file=@project.zip"
```

### Run Bash Script

```bash
curl -X POST http://localhost:8001/api/project/run-bash \
  -F "archive_file=@script.zip" \
  -F "main_script=run.sh"
```

### Get Artifacts

```bash
curl http://localhost:8001/api/projects/{project_id}/executions/{execution_id}/artifacts
```

## Supported Languages

| Language | Image | Entry Point |
|----------|-------|-------------|
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

## Project Structure

```
backend/
├── server.py           # FastAPI entry point
├── models.py           # Internal business models
├── views.py            # API request/response models
├── config.py           # Language runtime configuration
├── utils.py            # Utility functions
├── handlers/           # Language-specific handlers
│   ├── baseHandler.py  # Abstract base class
│   ├── resolver.py     # Handler resolver
│   └── python/, java/, go/, ...
├── services/           # Core services
│   ├── runner.py       # Code execution service
│   ├── docker.py       # Docker client
│   ├── collector.py    # Artifact collection
│   └── detector.py     # Project detection
├── docker/             # Docker images
└── tests/              # Test files
```

## Configuration

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `server_port` | `8001` | Server port |
| `MINIMAX_API_KEY` | - | API key for LLM services |

### Artifact Patterns

Edit `ALLOWED_ARTIFACT_PATTERNS` in `config.py` to customize which files can be collected as artifacts.

## Development

### Adding a New Language

1. **Update `config.py`**: Add language runtime configuration
2. **Create Docker Image**: Add Dockerfile in `docker/{language}/`
3. **Create Handler**: Implement `BaseHandler` subclass in `handlers/{language}/`
4. **Update `HandlerResolver`**: Register the new handler

### Running Tests

```bash
# Test project archive support
python tests/project_tester.py

# Test Bash script execution
python tests/bash_tester.py

# Test Jupyter notebook execution
python tests/jupyter_tester.py
```

## License

MIT
