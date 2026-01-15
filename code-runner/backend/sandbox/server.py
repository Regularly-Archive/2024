"""
Sandbox API server.

Provides the new sandbox-based runtime API for AI agents.
"""
from fastapi import FastAPI, HTTPException, Body, Query, Path
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import FileResponse
from fastapi.staticfiles import StaticFiles
from typing import Optional
from pathlib import Path as FilePath
import os

from sandbox.models import (
    TemplateListResponse, SandboxCreateRequest, SandboxResponse,
    SandboxDetailResponse, EnvironmentResponse, ExecRequest, ExecResponse,
    FileListResponse, FileContentResponse, ExportRequest, ExportResponse,
    DestroyResponse, ErrorResponse
)
from sandbox.runner import SandboxService
from services.logger import get_logger

logger = get_logger(__name__)

app = FastAPI(
    title="Sandbox Runtime API",
    description="AI-friendly sandbox runtime for code execution",
    version="1.0.0"
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Mount static files for artifacts
static_dir = FilePath(__file__).parent.parent / "static" / "projects"
if static_dir.exists():
    app.mount("/static/projects", StaticFiles(directory=str(static_dir)), name="projects")

# Create service
sandbox_service = SandboxService()


# ============ Template API ============

@app.get("/api/sandbox/templates", response_model=TemplateListResponse)
def list_templates():
    """List all available templates."""
    templates = sandbox_service.list_templates()
    return TemplateListResponse(templates=templates)


@app.get("/api/sandbox/templates/{template_id}")
def get_template(template_id: str):
    """Get a specific template."""
    try:
        template = sandbox_service.get_template(template_id)
        return template
    except ValueError as e:
        raise HTTPException(status_code=404, detail=str(e))


# ============ Sandbox Lifecycle API ============

@app.post("/api/sandbox/sandboxes", response_model=SandboxResponse)
def create_sandbox(request: SandboxCreateRequest = Body(...)):
    """
    Create a new sandbox from a template.

    This is the main entry point for the sandbox runtime.
    """
    response, error = sandbox_service.create_sandbox(request)
    if error:
        raise HTTPException(status_code=400, detail=error.model_dump())
    return response


@app.get("/api/sandbox/sandboxes", response_model=list)
def list_sandboxes():
    """List all running sandboxes."""
    return sandbox_service.list_sandboxes()


@app.get("/api/sandbox/sandboxes/{sandbox_id}", response_model=SandboxDetailResponse)
def get_sandbox(sandbox_id: str = Path(...)):
    """Get sandbox details."""
    response, error = sandbox_service.get_sandbox(sandbox_id)
    if error:
        raise HTTPException(status_code=404, detail=error.model_dump())
    return response


@app.delete("/api/sandbox/sandboxes/{sandbox_id}", response_model=DestroyResponse)
def destroy_sandbox(
    sandbox_id: str = Path(...),
    export: Optional[str] = Query(None, description="Path to export before destroying")
):
    """Destroy a sandbox."""
    response, error = sandbox_service.destroy_sandbox(sandbox_id, export)
    if error:
        raise HTTPException(status_code=400, detail=error.model_dump())
    return response


# ============ Environment Discovery API ============

@app.get("/api/sandbox/sandboxes/{sandbox_id}/env", response_model=EnvironmentResponse)
def get_environment(sandbox_id: str = Path(...)):
    """
    Get sandbox environment information.

    This endpoint is AI-friendly and provides information about
    what capabilities are available in the sandbox.
    """
    response, error = sandbox_service.get_environment(sandbox_id)
    if error:
        raise HTTPException(status_code=400, detail=error.model_dump())
    return response


# ============ Execution API ============

@app.post("/api/sandbox/sandboxes/{sandbox_id}/exec", response_model=ExecResponse)
def execute_command(
    sandbox_id: str = Path(...),
    request: ExecRequest = Body(...)
):
    """
    Execute a command in the sandbox.

    This is the primary execution endpoint. Commands are executed
    through bash, supporting shell features like pipes and redirects.
    """
    response, error = sandbox_service.execute(sandbox_id, request)
    if error:
        raise HTTPException(status_code=400, detail=error.model_dump())
    return response


# ============ Filesystem API ============

@app.get("/api/sandbox/sandboxes/{sandbox_id}/files", response_model=FileListResponse)
def list_files(
    sandbox_id: str = Path(...),
    path: str = Query(".", description="Directory path to list")
):
    """List files in sandbox."""
    response, error = sandbox_service.list_files(sandbox_id, path)
    if error:
        raise HTTPException(status_code=400, detail=error.model_dump())
    return response


@app.get("/api/sandbox/sandboxes/{sandbox_id}/file", response_model=FileContentResponse)
def read_file(
    sandbox_id: str = Path(...),
    path: str = Query(..., description="File path to read")
):
    """Read file content from sandbox."""
    response, error = sandbox_service.read_file(sandbox_id, path)
    if error:
        raise HTTPException(status_code=400, detail=error.model_dump())
    return response


@app.post("/api/sandbox/sandboxes/{sandbox_id}/write")
def write_file(
    sandbox_id: str = Path(...),
    path: str = Body(..., embed=True),
    content: str = Body(..., embed=True)
):
    """Write file to sandbox."""
    success, error = sandbox_service.write_file(sandbox_id, path, content)
    if not success:
        raise HTTPException(status_code=400, detail=error.model_dump() if error else "Write failed")
    return {"status": "ok", "path": path}


@app.post("/api/sandbox/sandboxes/{sandbox_id}/export", response_model=ExportResponse)
def export_files(
    sandbox_id: str = Path(...),
    request: ExportRequest = Body(...)
):
    """Export files from sandbox as artifact."""
    response, error = sandbox_service.export_workspace(sandbox_id, request.path)
    if error:
        raise HTTPException(status_code=400, detail=error.model_dump())
    return response


# ============ Sync/Upload API ============

@app.post("/api/sandbox/sandboxes/{sandbox_id}/upload")
def upload_workspace(
    sandbox_id: str = Path(...),
    clear_first: bool = Query(False, description="Clear workspace before uploading"),
    source_path: str = Body(..., description="Local directory or zip file path")
):
    """
    Upload local files to sandbox workspace.

    Useful for:
    - Re-uploading modified files after failed execution
    - Syncing local changes to sandbox
    """
    success, error = sandbox_service.upload_workspace(sandbox_id, source_path, clear_first)
    if not success:
        raise HTTPException(status_code=400, detail=error.model_dump() if error else "Upload failed")
    return {"status": "ok", "message": "Workspace uploaded successfully"}


@app.post("/api/sandbox/sandboxes/{sandbox_id}/sync")
def sync_files(
    sandbox_id: str = Path(...),
    files: Dict[str, str] = Body(..., description="Path mapping: sandbox_path -> local_path or content")
):
    """
    Sync multiple files to sandbox.

    Example:
    {
        "main.py": "./local/main.py",
        "config.json": "{\"debug\": true}"
    }
    """
    synced, error = sandbox_service.sync_files(sandbox_id, files)
    if error:
        raise HTTPException(status_code=400, detail=error.model_dump())
    return {"status": "ok", "synced": synced}


# ============ Artifact Download ============

@app.get("/api/sandbox/artifacts/{sandbox_id}/{artifact_filename}")
def download_artifact(
    sandbox_id: str = Path(...),
    artifact_filename: str = Path(...)
):
    """Download an artifact file."""
    # Artifact is stored in sandbox directory
    from sandbox.docker_service import SandboxDockerClient
    docker = SandboxDockerClient()
    sandbox_dir = docker.get_sandbox_dir(sandbox_id)
    artifact_path = FilePath(sandbox_dir) / artifact_filename

    if not artifact_path.exists():
        raise HTTPException(status_code=404, detail="Artifact not found")

    return FileResponse(
        path=str(artifact_path),
        media_type="application/zip",
        filename=artifact_filename
    )


# ============ Health Check ============

@app.get("/api/sandbox/health")
def health_check():
    """Health check endpoint."""
    return {
        "status": "healthy",
        "service": "sandbox-runtime",
        "version": "1.0.0"
    }


# ============ SDK Helper ============

@app.get("/api/sandbox/sdk")
def sdk_info():
    """
    SDK information and usage examples.
    """
    return {
        "name": "Sandbox SDK",
        "language": "Python (example)",
        "version": "1.0.0",
        "usage": {
            "create": """
from httpx import AsyncClient

async def main():
    async with AsyncClient(base_url="http://localhost:8001") as client:
        # Create sandbox
        resp = await client.post("/api/sandbox/sandboxes", json={
            "template": "python-basic"
        })
        sandbox = resp.json()
        sandbox_id = sandbox["sandbox_id"]

        # Execute commands
        resp = await client.post(f"/api/sandbox/sandboxes/{sandbox_id}/exec", json={
            "cmd": "python --version"
        })
        result = resp.json()

        # Destroy
        await client.delete(f"/api/sandbox/sandboxes/{sandbox_id}")

        return result
            """.strip(),
            "environment_check": """
# AI can first check environment
GET /api/sandbox/sandboxes/{id}/env

# Then decide what commands to run
POST /api/sandbox/sandboxes/{id}/exec
{
    "cmd": "python main.py"  # or any other command
}
            """.strip()
        }
    }


if __name__ == "__main__":
    import uvicorn
    port = int(os.environ.get('SANDBOX_PORT', '8002'))
    uvicorn.run(app, host="0.0.0.0", port=port)
