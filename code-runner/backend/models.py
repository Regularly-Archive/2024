"""
Internal business models for the sandbox.

These models are used for internal state management and are not directly bound to APIs.
"""
from pydantic import BaseModel, Field
from typing import Literal, List, Optional, Dict


# ============ Dependency Models ============

DependencyKind = Literal["manifest", "atomic"]
DependencyScope = Literal["project", "file"]
DependencySource = Literal["manifest_file", "file_header", "inline_cmd"]

class Dependency(BaseModel):
    language: str
    kind: DependencyKind
    scope: DependencyScope
    path: Optional[str] = None
    name: Optional[str] = None
    version: Optional[str] = None
    source: DependencySource


# ============ Project Models ============

class ProjectInfo(BaseModel):
    project_dir: str
    language: str
    files: List[str] = Field(default_factory=list)
    entry_point: Optional[str] = None
    dependencies: List[Dependency] = Field(default_factory=list)
    project_form: Optional[str] = None
    description: Optional[str] = None

    def has_dependencies(self) -> bool:
        return bool(self.dependencies)

    def has_dependencies(self, name: str) -> bool:
        return any(
            d.name == name or (d.kind == "manifest" and d.path and name in d.path)
            for d in self.dependencies
        )

    def get_inline_cmd_dependencies(self) -> List[Dependency]:
        return [
            d for d in self.dependencies
            if d.kind == "atomic" and d.source == "inline_cmd"
        ]


# ============ Runtime Models ============

class RuntimeInfo(BaseModel):
    image_name: str
    user: str
    container_id: Optional[str] = None
    environment: Optional[str] = None
    version: Optional[str] = None
    container_envs: Dict[str, str] = Field(default_factory=dict)
    runtime_args: str = ''


# ============ Execution Result Models ============

class StageResult(BaseModel):
    name: str
    command: str
    exit_code: int
    stdout: str
    stderr: str
    duration: float

    @property
    def status(self) -> str:
        return "success" if self.exit_code == 0 else "error"


class ExecutionResult(BaseModel):
    execution_id: str
    stages: List[StageResult] = Field(default_factory=list)
    total_duration: float = 0.0
    artifacts: list = Field(default_factory=list)

    @property
    def status(self) -> str:
        return "success" if all(s.exit_code == 0 for s in self.stages) else "error"

    @property
    def final_output(self) -> str:
        if not self.stages:
            return ""

        last = self.stages[-1]
        output = last.stdout if last.exit_code == 0 else last.stderr
        return output or ""


# ============ Artifact Model ============

class Artifact(BaseModel):
    name: str
    path: str
    size: int
    mime: Optional[str] = None
