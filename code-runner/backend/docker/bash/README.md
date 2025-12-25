# Bash Docker Environment

This Docker image provides a lightweight Alpine Linux environment with bash and common development tools.

## Features

- **Base Image**: Alpine Linux 3.19 (lightweight, ~5MB base)
- **Shell**: Bash with basic configuration
- **Tools Included**:
  - curl, wget, git
  - vim, nano
  - jq (JSON processor)
  - python3 and pip3
  - nodejs and npm
  - make
  - gcc, g++ (C/C++ compilers)
  - Basic development libraries

## Building the Image

```bash
cd /docker/bash
docker build -t code_runner/bash .
```

## Usage Examples

### Basic Bash Script Execution

```bash
# Run a simple bash script
docker run --rm -v $(pwd):/home/sandbox/src code_runner/bash bash src/hello.sh

# Interactive bash session
docker run -it --rm -v $(pwd):/home/sandbox/src code_runner/bash
```

### Running Scripts with Parameters

```bash
docker run --rm -v $(pwd):/home/sandbox/src code_runner/bash bash src/script.sh arg1 arg2
```

## Notes

- Scripts are executed as `sandbox` user (not root)
- Working directory is `/home/sandbox`
- Common directories like `bin`, `lib`, `src`, `tmp` are pre-created
- Bash prompt is configured for better visibility
- Includes aliases like `ll` for `ls -la`

## Security

The environment runs as a non-privileged user (`sandbox`) and includes basic Unix tools suitable for scripting tasks.