from fastapi import FastAPI, HTTPException, Body, UploadFile, File, Form, Request
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import FileResponse
import os
from typing import Optional, List
from config import LANGUAGE_RUNTIME_MAP
from utils import read_output, prepare_project_dir_from_code, prepare_project_dir_from_archive
from fastapi.staticfiles import StaticFiles
from pathlib import Path
import mimetypes

from models import RunCodeRequest, RunJupyterCodeCellRequest, RunCodeResponse, RunFilesRequest, ProjectArchiveResponse
from handlers.context import HandlerContext
from handlers.resolver import HandlerResolver
from services.runner import RunnerService
from services.detector import ProjectDetector
from views import SandboxResponseView, ArtifactView

app = FastAPI()
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)
app.mount("/static", StaticFiles(directory="./static", html=False), name="static")

projectDetector = ProjectDetector()

@app.post("/api/code/run", response_model=SandboxResponseView)
async def run_code(request: RunCodeRequest = Body(...), raw_request: Request = None):
    """
    运行代码片段
    """
    try:
        project_dir = prepare_project_dir_from_code(request.code, request.language, request.dependencies)
        project_info = projectDetector.build_project_info(project_dir, request.language, None, request.dependencies)
        ctx = HandlerContext.from_project(project_info)

        runnerService = RunnerService(ctx)
        runnerService.run()

        return SandboxResponseView.from_context(ctx, raw_request, content_type='text/plain')
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@app.post("/api/jupyter/run", response_model=SandboxResponseView)
async def run_jupyter(request: RunJupyterCodeCellRequest = Body(...), raw_request: Request = None):
    """
    运行 Jupyter 项目
    """
    try:
        project_dir = prepare_project_dir_from_code(request.code, request.language, request.dependencies)
        project_info = projectDetector.build_project_info(project_dir, request.language, None, request.dependencies)
        ctx = HandlerContext.from_project(project_info)
        
        language_config = LANGUAGE_RUNTIME_MAP[request.language]
        ctx.set_container_env('KERNEL_NAME', language_config['kernel'])
        ctx.set_container_env('NBCONVERT_OUTPUT_FORMAT', request.format)

        runnerService = RunnerService(ctx)
        runnerService.run()

        content_type = 'text/plain' if request.format == 'html' else 'text/notebook'
        output_file = 'output.html' if request.format == 'html' else 'output.ipynb'
        response = SandboxResponseView.from_context(ctx, raw_request, content_type=content_type)
        response.result.output = read_output(project_dir, output_file, '')
        return response
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@app.post("/api/project/run-archive", response_model=SandboxResponseView)
async def run_project_archive(
    archive_file: UploadFile = File(...),
    language: Optional[str] = Form(None),
    entry_point: Optional[str] = Form(None),
    build_command: Optional[str] = Form(None),
    run_command: Optional[str] = Form(None),
    dependencies: Optional[List[str]] = Form(None),
    raw_request: Request = None
):
    """
    运行项目
    """
    try:
        project_dir = await prepare_project_dir_from_archive(archive_file)

        ctx = HandlerContext.from_project(projectDetector.detect_project_info(project_dir))

        runnerService = RunnerService(ctx)
        runnerService.run()

        return SandboxResponseView.from_context(ctx, raw_request, content_type='text/plain')
    except Exception as e:
        import traceback
        print(traceback.print_exc())
        raise HTTPException(status_code=500, detail=str(e))

@app.post("/api/project/run-bash", response_model=RunCodeResponse)
async def run_bash_script(
    archive_file: UploadFile = File(...),
    main_script: str = Form(None),
    arguments: str = Form(None)
):
    """
    运行 Bash 项目
    """
    try:
        if not main_script or not main_script.strip():
            raise ValueError("The parameter main_script must be specified")
        
        project_dir = await prepare_project_dir_from_archive(archive_file)

        if not os.path.exists(os.path.join(project_dir, main_script)):
            raise ValueError(f"The main_script '{main_script}' must exists")

        ctx = HandlerContext.from_project(projectDetector.detect_project_info(project_dir, main_script))
        ctx.runtime_info.runtime_args = arguments or '' 

        runnerService = RunnerService(ctx)
        runnerService.run()

        return RunCodeResponse(
            output=ctx.execution_result.final_output, 
            content_type='text/plain', 
            duration=ctx.execution_result.total_duration, 
            language=ctx.language,
            project_info = ctx.project_info,
            runtime_info = ctx.runtime_info
        )
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Failed to run project archive: {str(e)}")

@app.get("/api/projects/{project_id}/executions/{execution_id}/artifacts", response_model=List[ArtifactView])
def get_artifacts(raw_request: Request, project_id: str, execution_id: str) -> List[ArtifactView]:
    static_root = Path("./static/projects") / project_id / "executions" / execution_id / "artifacts"
    if not static_root.exists():
        return []
    
    artifacts = []
    for file_path in static_root.rglob("*"):
        if file_path.is_file():
            artifacts.append(ArtifactView.from_file(project_id, execution_id, file_path, raw_request))

    return artifacts

@app.get("/api/projects/{project_id}/executions/{execution_id}/artifacts/{artifact_path:path}")
def get_artifact(project_id: str, execution_id: str, artifact_path: str) -> FileResponse:
    static_root = (Path("./static/projects") / project_id / "executions" / execution_id / "artifacts").resolve()
    file_path = (static_root / artifact_path).resolve() 

    if not str(file_path).startswith(str(static_root)):
        raise HTTPException(status_code=403, detail="Invalid artifact path")

    if not file_path.exists() or not file_path.is_file():
        raise HTTPException(status_code=404, detail="Artifact not found")

    mime, _ = mimetypes.guess_type(file_path)

    return FileResponse(
        path=file_path,
        media_type=mime or "application/octet-stream",
        filename=file_path.name, 
    )

if __name__ == "__main__":
    import uvicorn
    port = int(os.environ.get('server_port', '8001'))
    uvicorn.run(app, host="0.0.0.0", port=port)
