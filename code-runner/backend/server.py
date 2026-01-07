from fastapi import FastAPI, HTTPException, Body, UploadFile, File, Form
from fastapi.middleware.cors import CORSMiddleware
import time
import os
import tempfile
from typing import Optional, List
from config import LANGUAGE_RUNTIME_MAP
from utils import read_output, prepare_project_dir_from_code, prepare_project_dir_from_archive

from models import RunCodeRequest, RunJupyterCellRequest, RunCodeResponse, RunFilesRequest, ProjectArchiveResponse
import traceback
import shutil
from handlers.context import HandlerContext
from handlers.resolver import HandlerResolver
from services.runner import RunnerService
from services.detector import ProjectDetector

app = FastAPI()
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

projectDetector = ProjectDetector()
handlerResolver = HandlerResolver()
runnerService = RunnerService()

@app.post("/api/code/run", response_model=RunCodeResponse)
async def run_code(request: RunCodeRequest = Body(...)):
    """
    运行代码片段
    """
    try:
        project_dir = prepare_project_dir_from_code(request.code, request.language, request.dependencies)
        project_info = projectDetector.build_project_info(project_dir, request.language, None, request.dependencies)
        ctx = HandlerContext.from_project(project_info)

        handler = handlerResolver.resolve(ctx)
        runnerService.run(handler)

        return RunCodeResponse(
            output=ctx.execution_result.final_output, 
            contentType='text/plain', 
            duration=ctx.execution_result.total_duration, 
            language=ctx.language,
            project_info = ctx.project_info,
            runtime_info = ctx.runtime_info
        )
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@app.post("/api/jupyter/run", response_model=RunCodeResponse)
async def run_jupyter(request: RunJupyterCellRequest = Body(...)):
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

        handler = handlerResolver.resolve(ctx)
        runnerService.run(handler)

        return RunCodeResponse(
            output=read_output(project_dir,''),
            contentType='text/plain' if request.format == 'html' else 'text/notebook', 
            duration=ctx.execution_result.total_duration, 
            language=ctx.language,
            project_info = ctx.project_info,
            runtime_info = ctx.runtime_info
        )
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@app.post("/api/project/run-archive", response_model=ProjectArchiveResponse)
async def run_project_archive(
    archive_file: UploadFile = File(...),
    language: Optional[str] = Form(None),
    entry_point: Optional[str] = Form(None),
    build_command: Optional[str] = Form(None),
    run_command: Optional[str] = Form(None),
    dependencies: Optional[List[str]] = Form(None)
):
    """
    运行项目
    """
    try:
        project_dir = await prepare_project_dir_from_archive(archive_file)

        ctx = HandlerContext.from_project(projectDetector.detect_project_info(project_dir))

        handler = handlerResolver.resolve(ctx)
        runnerService.run(handler)

        return RunCodeResponse(
            output=ctx.execution_result.final_output, 
            contentType='text/plain', 
            duration=ctx.execution_result.total_duration, 
            language=ctx.language,
            project_info = ctx.project_info,
            runtime_info = ctx.runtime_info
        )
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

        handler = handlerResolver.resolve(ctx)
        runnerService.run(handler)

        return RunCodeResponse(
            output=ctx.execution_result.final_output, 
            contentType='text/plain', 
            duration=ctx.execution_result.total_duration, 
            language=ctx.language,
            project_info = ctx.project_info,
            runtime_info = ctx.runtime_info
        )
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Failed to run project archive: {str(e)}")

if __name__ == "__main__":
    import uvicorn
    port = int(os.environ.get('server_port', '8001'))
    uvicorn.run(app, host="0.0.0.0", port=port)
