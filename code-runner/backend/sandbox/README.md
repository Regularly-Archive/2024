# Sandbox Runtime API

A new AI-friendly sandbox runtime based on the sandbox/template model.

## Overview

This module provides a new API design that is more suitable for AI agents:

- **Template-based**: Define machine capabilities as templates
- **Stateful sandboxes**: Create, use, and destroy sandbox instances
- **Bash-first execution**: All commands go through a single `exec` endpoint
- **Environment discovery**: AI can query what capabilities are available

## Architecture

```
Template  ── resolve ──▶ Runtime/Image
                           │
                           ▼
                        Sandbox
                           │
                           ▼
                        exec(bash)
                           │
                           ▼
                        Artifacts
```

## API Endpoints

### Template API

```
GET /api/sandbox/templates          # List all templates
GET /api/sandbox/templates/{id}     # Get template details
```

### Sandbox Lifecycle

```
POST /api/sandbox/sandboxes         # Create sandbox
GET /api/sandbox/sandboxes          # List running sandboxes
GET /api/sandbox/sandboxes/{id}     # Get sandbox details
DELETE /api/sandbox/sandboxes/{id}  # Destroy sandbox
```

### Execution

```
POST /api/sandbox/sandboxes/{id}/exec   # Execute command
```

### Environment Discovery

```
GET /api/sandbox/sandboxes/{id}/env     # Get environment info
```

### Filesystem

```
GET /api/sandbox/sandboxes/{id}/files       # List directory
GET /api/sandbox/sandboxes/{id}/file        # Read file
POST /api/sandbox/sandboxes/{id}/write      # Write file
POST /api/sandbox/sandboxes/{id}/export     # Export as artifact
```

## Usage

### Using the Python Client

```python
import asyncio
from sandbox_client import SandboxClient

async def main():
    async with SandboxClient() as client:
        # Create sandbox
        sandbox = await client.create_sandbox("python-basic")

        # Check environment
        env = await client.get_environment(sandbox.sandbox_id)
        print(f"Capabilities: {env.capabilities}")

        # Execute commands
        result = await client.exec(sandbox.sandbox_id, "python --version")
        print(f"Output: {result.stdout}")

        # Destroy
        await client.destroy(sandbox.sandbox_id)

asyncio.run(main())
```

### Using curl

```bash
# Create a sandbox
curl -X POST http://localhost:8002/api/sandbox/sandboxes \
  -H "Content-Type: application/json" \
  -d '{"template": "python-basic"}'

# Get environment
curl http://localhost:8002/api/sandbox/sandboxes/{id}/env

# Execute command
curl -X POST http://localhost:8002/api/sandbox/sandboxes/{id}/exec \
  -H "Content-Type: application/json" \
  -d '{"cmd": "python --version"}'

# Destroy sandbox
curl -X DELETE http://localhost:8002/api/sandbox/sandboxes/{id}
```

## Templates

| Template | Description | Capabilities |
|----------|-------------|--------------|
| `python-basic` | General-purpose Python | bash, python@3.11, pip |
| `python-data` | Python for data science | bash, python@3.11, numpy, pandas, jupyter |
| `node-basic` | Node.js runtime | bash, node@20, npm |
| `dotnet-basic` | .NET runtime | bash, dotnet@8 |
| `cpp-basic` | C/C++ development | bash, gcc@11, g++, make |
| `java-basic` | Java runtime | bash, java@20, maven |
| `rust-basic` | Rust development | bash, rustc@1.81, cargo |
| `go-basic` | Go development | bash, go@latest |
| `linux-basic` | Basic Linux | bash, coreutils, git |

## Running the Server

```bash
# Run sandbox server on port 8002
python -m sandbox.server

# Or with custom port
SANDBOX_PORT=8080 python -m sandbox.server
```

## Differences from Old API

| Old API | New API |
|---------|---------|
| `language` field | `template` with `capabilities` |
| `project` based | `sandbox` based |
| `install/build/run` stages | `exec(cmd)` - any command |
| Stateless | Stateful (create → exec → destroy) |
| No environment query | `GET /env` for discovery |

## AI Integration

The new API is designed for AI agents:

1. **Environment Discovery**: AI can query `/env` to know what tools are available
2. **Flexible Execution**: No fixed stages - AI can run any command sequence
3. **Stateful Sessions**: Keep a sandbox alive for multiple operations
4. **File Operations**: Read/write files for complex workflows

Example AI workflow:

```python
# AI checks environment first
env = await client.get_environment(sandbox_id)

# AI decides what to do based on capabilities
if "python" in str(env.capabilities):
    result = await client.exec(sandbox_id, "python main.py")
else:
    result = await client.exec(sandbox_id, "node main.js")
```
