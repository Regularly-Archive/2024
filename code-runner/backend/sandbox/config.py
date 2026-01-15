"""
Template definitions for the sandbox runtime.

Each template represents a class of machine capabilities - a stable contract
that can be resolved to a Docker image.
"""
from typing import Dict, Any, List
from sandbox.models import Template

# Template ID -> Template definition
# The template is a stable contract; the image can be updated
TEMPLATES: Dict[str, Template] = {
    "python-basic": Template(
        id="python-basic",
        description="General-purpose Python runtime",
        capabilities=[
            "bash",
            "python@3.11",
            "pip",
            "coreutils"
        ],
        defaults={
            "workdir": "/workspace",
            "shell": "/bin/bash"
        },
        constraints={
            "network": False,
            "max_exec_time": "30m"
        }
    ),
    "python-data": Template(
        id="python-data",
        description="Python environment for data science",
        capabilities=[
            "bash",
            "python@3.11",
            "pip",
            "numpy",
            "pandas",
            "jupyter",
            "coreutils"
        ],
        defaults={
            "workdir": "/workspace",
            "shell": "/bin/bash"
        },
        constraints={
            "network": False,
            "max_exec_time": "1h"
        }
    ),
    "node-basic": Template(
        id="node-basic",
        description="Node.js runtime",
        capabilities=[
            "bash",
            "node@20",
            "npm",
            "coreutils"
        ],
        defaults={
            "workdir": "/workspace",
            "shell": "/bin/bash"
        },
        constraints={
            "network": False,
            "max_exec_time": "30m"
        }
    ),
    "dotnet-basic": Template(
        id="dotnet-basic",
        description=".NET runtime",
        capabilities=[
            "bash",
            "dotnet@8",
            "coreutils"
        ],
        defaults={
            "workdir": "/workspace",
            "shell": "/bin/bash"
        },
        constraints={
            "network": False,
            "max_exec_time": "30m"
        }
    ),
    "cpp-basic": Template(
        id="cpp-basic",
        description="C/C++ development environment",
        capabilities=[
            "bash",
            "gcc@11",
            "g++",
            "make",
            "coreutils"
        ],
        defaults={
            "workdir": "/workspace",
            "shell": "/bin/bash"
        },
        constraints={
            "network": False,
            "max_exec_time": "30m"
        }
    ),
    "java-basic": Template(
        id="java-basic",
        description="Java runtime",
        capabilities=[
            "bash",
            "java@20",
            "maven",
            "coreutils"
        ],
        defaults={
            "workdir": "/workspace",
            "shell": "/bin/bash"
        },
        constraints={
            "network": False,
            "max_exec_time": "30m"
        }
    ),
    "rust-basic": Template(
        id="rust-basic",
        description="Rust development environment",
        capabilities=[
            "bash",
            "rustc@1.81",
            "cargo",
            "coreutils"
        ],
        defaults={
            "workdir": "/workspace",
            "shell": "/bin/bash"
        },
        constraints={
            "network": False,
            "max_exec_time": "30m"
        }
    ),
    "go-basic": Template(
        id="go-basic",
        description="Go development environment",
        capabilities=[
            "bash",
            "go@latest",
            "coreutils"
        ],
        defaults={
            "workdir": "/workspace",
            "shell": "/bin/bash"
        },
        constraints={
            "network": False,
            "max_exec_time": "30m"
        }
    ),
    "linux-basic": Template(
        id="linux-basic",
        description="Basic Linux environment",
        capabilities=[
            "bash",
            "coreutils",
            "git"
        ],
        defaults={
            "workdir": "/workspace",
            "shell": "/bin/bash"
        },
        constraints={
            "network": False,
            "max_exec_time": "30m"
        }
    ),
    "jupyter-python": Template(
        id="jupyter-python",
        description="Jupyter Python environment",
        capabilities=[
            "bash",
            "python@3.11",
            "jupyter",
            "pip",
            "coreutils"
        ],
        defaults={
            "workdir": "/workspace",
            "shell": "/bin/bash"
        },
        constraints={
            "network": False,
            "max_exec_time": "1h"
        }
    ),
}


# Template ID -> Docker image mapping
# This allows updating images without changing the template contract
TEMPLATE_IMAGES: Dict[str, str] = {
    "python-basic": "code_runner/python3",
    "python-data": "code_runner/python3",
    "node-basic": "code_runner/nodejs",
    "dotnet-basic": "code_runner/dotnet",
    "cpp-basic": "code_runner/cpp",
    "java-basic": "code_runner/java",
    "rust-basic": "code_runner/rust",
    "go-basic": "code_runner/go",
    "linux-basic": "code_runner/bash",
    "jupyter-python": "code_runner/jupyterlab",
}


def resolve_image(template_id: str) -> str:
    """Resolve a template ID to a Docker image."""
    return TEMPLATE_IMAGES.get(template_id, f"code_runner/{template_id}")


def get_template(template_id: str) -> Template:
    """Get a template by ID."""
    if template_id not in TEMPLATES:
        raise ValueError(f"Unknown template: {template_id}")
    return TEMPLATES[template_id]


def list_templates() -> List[Template]:
    """List all available templates."""
    return list(TEMPLATES.values())


def resolve_capabilities(image_name: str) -> List[str]:
    """
    Resolve capabilities from a Docker image.

    In a production system, this might query the container's
    installed packages. For now, we use a static mapping.
    """
    capability_map: Dict[str, List[str]] = {
        "code_runner/python3": ["bash", "python@3.9", "pip"],
        "code_runner/nodejs": ["bash", "node@18", "npm"],
        "code_runner/dotnet": ["bash", "dotnet@8"],
        "code_runner/cpp": ["bash", "gcc@11", "g++", "make"],
        "code_runner/java": ["bash", "java@20", "maven"],
        "code_runner/rust": ["bash", "rustc@1.81", "cargo"],
        "code_runner/go": ["bash", "go@latest"],
        "code_runner/bash": ["bash", "coreutils", "git"],
        "code_runner/jupyterlab": ["bash", "python@3.11", "jupyter", "pip"],
    }
    return capability_map.get(image_name, ["bash"])
