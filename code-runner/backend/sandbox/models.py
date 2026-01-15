"""
Sandbox models for the new AI-friendly runtime.

These models define the core objects: Template, Sandbox, Execution.
"""
from pydantic import BaseModel, Field
from typing import Literal, List, Optional, Dict, Any
from datetime import datetime
from enum import Enum


class SandboxStatus(str, Enum):
    CREATING = "creating"
    RUNNING = "running"
    TERMINATED = "terminated"
    ERROR = "error"


class Template(BaseModel):
    """Template defines a class of machine capabilities - a stable contract."""
    id: str
    description: str
    capabilities: List[str] = Field(default_factory=list)
    defaults: Dict[str, str] = Field(default_factory=dict)
    constraints: Dict[str, Any] = Field(default_factory=dict)

    @property
    def workdir(self) -> str:
        return self.defaults.get("workdir", "/workspace")

    @property
    def shell(self) -> str:
        return self.defaults.get("shell", "/bin/bash")


class TemplateListResponse(BaseModel):
    """Response for listing templates."""
    templates: List[Template]


class RuntimeInfo(BaseModel):
    """Runtime information resolved from a template."""
    image: str
    resolved_from: str
    os: Optional[str] = None
    arch: Optional[str] = None


class SandboxCreateRequest(BaseModel):
    """Request to create a new sandbox."""
    template: str
    workspace: Optional[Dict[str, str]] = None
    resources: Optional[Dict[str, Any]] = None


class SandboxResponse(BaseModel):
    """Response after creating a sandbox."""
    sandbox_id: str
    status: SandboxStatus
    runtime: RuntimeInfo
    paths: Dict[str, str]
    created_at: datetime


class SandboxDetailResponse(BaseModel):
    """Full sandbox details including runtime info."""
    sandbox_id: str
    template: str
    status: SandboxStatus
    runtime: RuntimeInfo
    paths: Dict[str, str]
    created_at: datetime
    expires_at: Optional[datetime] = None


class EnvironmentResponse(BaseModel):
    """Environment discovery response - AI friendly."""
    os: str
    arch: str
    capabilities: List[str]
    paths: Dict[str, str]


class ExecRequest(BaseModel):
    """Request to execute a command in sandbox."""
    cmd: str
    cwd: Optional[str] = None
    env: Dict[str, str] = Field(default_factory=dict)


class ExecResponse(BaseModel):
    """Response from executing a command."""
    execution_id: str
    exit_code: int
    stdout: str
    stderr: str
    duration_ms: float
    files_changed: List[str] = Field(default_factory=list)


class FileItem(BaseModel):
    """A file or directory in the sandbox."""
    name: str
    path: str
    is_dir: bool
    size: Optional[int] = None


class FileListResponse(BaseModel):
    """Response for listing files."""
    path: str
    items: List[FileItem]


class FileContentResponse(BaseModel):
    """Response for file content."""
    path: str
    content: str
    size: int


class ExportRequest(BaseModel):
    """Request to export files from sandbox."""
    path: str
    as_artifact: bool = True


class ExportResponse(BaseModel):
    """Response after exporting files."""
    artifact_id: str
    path: str
    size: int
    download_url: str


class DestroyResponse(BaseModel):
    """Response after destroying a sandbox."""
    sandbox_id: str
    status: str
    artifact_exported: bool = False
    artifact_id: Optional[str] = None


class ErrorResponse(BaseModel):
    """Standard error response."""
    error: str
    detail: Optional[str] = None
