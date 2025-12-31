from pydantic import BaseModel, Field
from typing import Literal, List, Optional, Dict, Any

class RunCodeRequest(BaseModel):
    code: str
    language: str
    dependencies: list[str] = []

class RunJupyterCellRequest(BaseModel):
    code: str
    language: str
    dependencies: list[str] = []
    format: Literal['html', 'notebook'] = Field('html', description='The output format for jupyter runner')

class RunCodeResponse(BaseModel):
    output: str
    contentType: str
    duration: float
    language: str

class CodeFile(BaseModel):
    path: str
    content: str

class RunFilesRequest(BaseModel):
    language: str
    files: List[CodeFile]
    dependencies: Optional[List[str]] = []
    entry_path: Optional[str] = None


class ProjectArchiveRequest(BaseModel):
    language: Optional[str] = None
    entry_point: Optional[str] = None
    build_command: Optional[str] = None
    run_command: Optional[str] = None
    max_archive_size: int = 50  # MB

class ProjectInfo(BaseModel):
    project_dir: str
    language: str
    files: List[str] = Field(default_factory=list)
    entry_point: Optional[str] = None
    dependencies: List[str] = Field(default_factory=list)
    project_form: Optional[str] = None 
    description: Optional[str] = None


class RuntimeInfo(BaseModel):
    image_name: str
    user: str
    container_id: Optional[str] = None
    environment: Optional[str] = None
    container_envs: Dict[str, str] = Field(default_factory=dict)
    runtime_args: str = ''

class ProjectArchiveResponse(RunCodeResponse):
    detected_language: Optional[str] = None
    detected_entry_point: Optional[str] = None
    build_output: Optional[str] = None
    project_info: ProjectInfo = None
    runtime_info: dict = Field(default_factory=dict)

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
    stages: List[StageResult] = Field(default_factory=list)
    total_duration: float = 0.0

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
