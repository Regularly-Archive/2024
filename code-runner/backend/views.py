import mimetypes
from pydantic import BaseModel, Field
from typing import Optional
from models import Artifact
from pathlib import Path
from fastapi import Request

class RuntimeInfoView(BaseModel):
    language: str
    environment: Optional[str] = None
    version: Optional[str] = None
    kernel: Optional[str] = None

class ProjectInfoView(BaseModel):
    project_id: Optional[str] = None
    project_name: Optional[str] = None 

class ArtifactView(Artifact):
    url: str

    @classmethod
    def from_file(cls, project_id: str, execution_id: str, file_path: Path, request: Request):
        base_url = str(request.base_url).rstrip("/")
        rel_path = file_path.relative_to(Path('./static') / "projects" / project_id / "executions" / execution_id / "artifacts")
        stat = file_path.stat()
        mime, _ = mimetypes.guess_type(file_path)
        return cls(
            name=file_path.name,
            path=str(rel_path.as_posix()),
            size=stat.st_size,
            mime=mime or "application/octet-stream",
            url=f"{base_url}/api/projects/{project_id}/executions/{execution_id}/artifacts/{rel_path.as_posix()}"
        )
    
    @classmethod
    def from_artifact(cls, artifact: Artifact, project_id: str, execution_id: str, request: Request):
        base_url = str(request.base_url).rstrip("/")
        return cls(
            name=artifact.name,
            path=str(Path(artifact.path).as_posix()),
            size=artifact.size,
            mime=artifact.mime,
            url=f"{base_url}/api/projects/{project_id}/executions/{execution_id}/artifacts/{Path(artifact.path).as_posix()}"
        )

class ExecutionResultView(BaseModel):
    output: str
    content_type: str
    duration: float
    artifacts: list[ArtifactView] = Field(default_factory=list)
    execution_id: str

class SandboxResponseView(BaseModel):
    result: ExecutionResultView
    runtime: RuntimeInfoView
    project: ProjectInfoView

    @classmethod
    def from_context(cls, ctx, request: Request, content_type='text/plain'):
        runtime_info = RuntimeInfoView(
            language=ctx.language,
            environment=ctx.runtime_info.environment,
            version=ctx.runtime_info.version,
            kernel=ctx.runtime_info.container_envs.get('KERNEL_NAME', None)
        )
        project_info = ProjectInfoView(
            project_id=ctx.project_id,
            project_name=ctx.project_name
        )
        execution_id = ctx.execution_result.execution_id
        artifacts = [
            ArtifactView.from_artifact(artifact, ctx.project_id, execution_id, request) for artifact in ctx.execution_result.artifacts
        ]
        execution_result = ExecutionResultView(
            output=ctx.execution_result.final_output,
            content_type=content_type,
            duration=ctx.execution_result.total_duration,
            artifacts=artifacts,
            execution_id=execution_id
        )
        return cls(
            result=execution_result,
            runtime=runtime_info,
            project=project_info
        )