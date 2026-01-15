"""
Sandbox service for managing sandbox lifecycle and execution.

This service provides the core operations for the sandbox runtime:
- Create sandbox from template
- Execute commands
- Manage files
- Destroy sandbox
"""
import os
import time
import uuid
import shutil
import hashlib
import zipfile
import json
from datetime import datetime, timedelta
from typing import Optional, Dict, List, Tuple, Any
from dataclasses import dataclass, field

from sandbox.models import (
    Template, SandboxStatus, SandboxCreateRequest, SandboxResponse,
    SandboxDetailResponse, EnvironmentResponse, ExecRequest, ExecResponse,
    FileItem, FileListResponse, FileContentResponse, ExportRequest, ExportResponse,
    DestroyResponse, ErrorResponse
)
from sandbox.config import (
    TEMPLATES, TEMPLATE_IMAGES, get_template, resolve_image, list_templates,
    resolve_capabilities
)
from sandbox.docker_service import SandboxDockerClient
from sandbox.storage import SandboxRepository
from services.logger import get_logger

logger = get_logger(__name__)


@dataclass
class SandboxInstance:
    """Internal representation of a running sandbox."""
    sandbox_id: str
    template_id: str
    container_id: str
    image_name: str
    workdir: str
    user: str
    status: SandboxStatus
    created_at: datetime
    expires_at: datetime
    file_hashes: Dict[str, str] = field(default_factory=dict)

    def is_expired(self) -> bool:
        """Check if the sandbox has expired."""
        return datetime.now() > self.expires_at


class SandboxService:
    """Service for managing sandboxes with persistent storage."""

    def __init__(self, repository: SandboxRepository = None):
        self.docker = SandboxDockerClient()
        self.repo = repository or SandboxRepository()
        self._instances: Dict[str, SandboxInstance] = {}  # In-memory cache

        # Recover orphaned sandboxes on startup
        self._recover_sandboxes()

    def _recover_sandboxes(self):
        """Recover sandboxes from storage on startup."""
        logger.info("Recovering sandboxes from storage...")

        # Clean up expired records first
        expired_count = self.repo.cleanup_expired()
        if expired_count > 0:
            logger.info(f"Cleaned up {expired_count} expired records")

        # Recover running sandboxes
        result = self.repo.recover_orphaned()
        logger.info(
            f"Recovery complete: {result['recovered']} recovered, "
            f"{result['terminated']} marked as terminated"
        )

    def _generate_sandbox_id(self) -> str:
        """Generate a unique sandbox ID."""
        return f"sbx_{uuid.uuid4().hex[:12]}"

    def _generate_execution_id(self) -> str:
        """Generate a unique execution ID."""
        return f"exec_{uuid.uuid4().hex[:12]}"

    def _get_instance(self, sandbox_id: str) -> Optional[SandboxInstance]:
        """Get sandbox instance from cache or storage."""
        # Check cache first
        if sandbox_id in self._instances:
            return self._instances[sandbox_id]

        # Try to load from storage
        instance = self.repo.load(sandbox_id)
        if instance:
            self._instances[sandbox_id] = instance
        return instance

    def _save_instance(self, instance: SandboxInstance) -> bool:
        """Save instance to storage and cache."""
        self._instances[instance.sandbox_id] = instance
        return self.repo.save(instance)

    def _remove_instance(self, sandbox_id: str) -> bool:
        """Remove instance from cache and storage."""
        self._instances.pop(sandbox_id, None)
        return self.repo.destroy(sandbox_id)

    def _mark_expired(self):
        """Mark expired sandboxes."""
        for sandbox_id, instance in list(self._instances.items()):
            if instance.is_expired():
                instance.status = SandboxStatus.TERMINATED
                self.repo.storage.update_status(sandbox_id, "terminated")

    def list_templates(self) -> List[Template]:
        """List all available templates."""
        return list_templates()

    def get_template(self, template_id: str) -> Template:
        """Get a template by ID."""
        return get_template(template_id)

    def create_sandbox(self, request: SandboxCreateRequest) -> Tuple[Optional[SandboxResponse], Optional[ErrorResponse]]:
        """
        Create a new sandbox from a template.

        Args:
            request: Sandbox creation request

        Returns:
            Tuple of (response, error)
        """
        # Validate template
        try:
            template = get_template(request.template)
        except ValueError as e:
            return None, ErrorResponse(error="Invalid template", detail=str(e))

        sandbox_id = self._generate_sandbox_id()
        image_name = resolve_image(request.template)
        workdir = template.workdir
        user = "sandbox"

        # Set expiry time
        max_time = template.constraints.get("max_exec_time", "30m")
        expires_at = self._parse_timeout(max_time)

        # Create container
        try:
            container = self.docker.create_container(
                sandbox_id=sandbox_id,
                image_name=image_name,
                workdir=workdir,
                envs={}
            )
        except Exception as e:
            logger.error("Failed to create container: %s", e)
            return None, ErrorResponse(error="Container creation failed", detail=str(e))

        # Upload workspace if provided
        if request.workspace and "files" in request.workspace:
            source = request.workspace["files"]
            if source.startswith("artifact://"):
                artifact_id = source[10:]
                artifact_path = self._resolve_artifact(artifact_id)
                if artifact_path:
                    self.docker.upload_workspace(sandbox_id, artifact_path)

        # Take initial file snapshot
        file_hashes = self.docker.snapshot_files(sandbox_id)

        # Store sandbox instance
        instance = SandboxInstance(
            sandbox_id=sandbox_id,
            template_id=request.template,
            container_id=container.id,
            image_name=image_name,
            workdir=workdir,
            user=user,
            status=SandboxStatus.RUNNING,
            created_at=datetime.now(),
            expires_at=expires_at,
            file_hashes=file_hashes
        )

        # Save to storage
        self._save_instance(instance)
        logger.info(f"Sandbox {sandbox_id} created from template {request.template}")

        response = SandboxResponse(
            sandbox_id=sandbox_id,
            status=SandboxStatus.RUNNING,
            runtime={
                "image": image_name,
                "resolved_from": f"template:{request.template}"
            },
            paths={
                "workspace": workdir
            },
            created_at=instance.created_at
        )

        return response, None

    def get_sandbox(self, sandbox_id: str) -> Tuple[Optional[SandboxDetailResponse], Optional[ErrorResponse]]:
        """
        Get sandbox details.

        Args:
            sandbox_id: Sandbox ID

        Returns:
            Tuple of (response, error)
        """
        instance = self._get_instance(sandbox_id)
        if not instance:
            return None, ErrorResponse(error="Sandbox not found", detail=f"ID: {sandbox_id}")

        # Check expiry
        self._mark_expired()

        # Get container status
        container = self.docker.get_container_by_id(instance.container_id)
        if container and container.status != "running":
            instance.status = SandboxStatus.TERMINATED

        # Check expiry
        if instance.is_expired():
            instance.status = SandboxStatus.TERMINATED
            self.repo.storage.update_status(sandbox_id, "terminated")

        return SandboxDetailResponse(
            sandbox_id=sandbox_id,
            template=instance.template_id,
            status=instance.status,
            runtime={
                "image": instance.image_name,
                "resolved_from": f"template:{instance.template_id}"
            },
            paths={
                "workspace": instance.workdir
            },
            created_at=instance.created_at,
            expires_at=instance.expires_at
        ), None

    def get_environment(self, sandbox_id: str) -> Tuple[Optional[EnvironmentResponse], Optional[ErrorResponse]]:
        """
        Get sandbox environment - AI friendly discovery.

        Args:
            sandbox_id: Sandbox ID

        Returns:
            Tuple of (response, error)
        """
        instance = self._get_instance(sandbox_id)
        if not instance:
            return None, ErrorResponse(error="Sandbox not found", detail=f"ID: {sandbox_id}")

        if instance.status != SandboxStatus.RUNNING:
            return None, ErrorResponse(error="Sandbox not running", detail=f"Status: {instance.status}")

        container = self.docker.get_container_by_id(instance.container_id)
        if not container:
            return None, ErrorResponse(error="Container not found", detail="Container may have been removed")

        # Get capabilities from image
        capabilities = resolve_capabilities(instance.image_name)

        # Try to detect actual installed versions in the container
        try:
            exit_code, stdout, _ = self.docker.exec_command(
                container,
                "python3 --version 2>/dev/null || python --version 2>/dev/null || echo 'none'",
                instance.user,
                instance.workdir
            )
            if exit_code == 0 and "Python" in stdout:
                py_version = stdout.strip().split()[1]
                capabilities = [c if not c.startswith("python@") else f"python@{py_version}" for c in capabilities]
        except Exception:
            pass

        return EnvironmentResponse(
            os="linux",
            arch="amd64",
            capabilities=capabilities,
            paths={
                "workspace": instance.workdir
            }
        ), None

    def execute(
        self,
        sandbox_id: str,
        request: ExecRequest
    ) -> Tuple[Optional[ExecResponse], Optional[ErrorResponse]]:
        """
        Execute a command in the sandbox.

        Args:
            sandbox_id: Sandbox ID
            request: Execution request

        Returns:
            Tuple of (response, error)
        """
        instance = self._get_instance(sandbox_id)
        if not instance:
            return None, ErrorResponse(error="Sandbox not found", detail=f"ID: {sandbox_id}")

        if instance.status != SandboxStatus.RUNNING:
            return None, ErrorResponse(error="Sandbox not running", detail=f"Status: {instance.status}")

        if instance.is_expired():
            instance.status = SandboxStatus.TERMINATED
            self.repo.storage.update_status(sandbox_id, "terminated")
            return None, ErrorResponse(error="Sandbox expired", detail="Please create a new sandbox")

        container = self.docker.get_container_by_id(instance.container_id)
        if not container:
            instance.status = SandboxStatus.TERMINATED
            self.repo.storage.update_status(sandbox_id, "terminated")
            return None, ErrorResponse(error="Container not found", detail="Container may have been removed")

        # Determine working directory
        cwd = request.cwd or instance.workdir

        # Build environment
        env = request.env or {}

        # Start timing
        start_time = time.time()

        # Execute command
        try:
            exit_code, stdout, stderr = self.docker.exec_command(
                container,
                request.cmd,
                instance.user,
                cwd
            )
        except Exception as e:
            return None, ErrorResponse(error="Execution failed", detail=str(e))

        duration_ms = (time.time() - start_time) * 1000

        # Scan for changed files
        files_changed = self.docker.scan_files_changed(sandbox_id, instance.file_hashes)

        # Update file snapshot
        instance.file_hashes = self.docker.snapshot_files(sandbox_id)

        # Save updated state to storage
        self._save_instance(instance)

        execution_id = self._generate_execution_id()

        response = ExecResponse(
            execution_id=execution_id,
            exit_code=exit_code,
            stdout=stdout,
            stderr=stderr,
            duration_ms=round(duration_ms, 2),
            files_changed=files_changed
        )

        return response, None

    def list_files(self, sandbox_id: str, path: str) -> Tuple[Optional[FileListResponse], Optional[ErrorResponse]]:
        """
        List files in sandbox.

        Args:
            sandbox_id: Sandbox ID
            path: Directory path

        Returns:
            Tuple of (response, error)
        """
        instance = self._get_instance(sandbox_id)
        if not instance:
            return None, ErrorResponse(error="Sandbox not found", detail=f"ID: {sandbox_id}")

        items = self.docker.list_directory(sandbox_id, path)

        file_items = [
            FileItem(
                name=item["name"],
                path=item["path"],
                is_dir=item["is_dir"],
                size=item["size"]
            )
            for item in items
        ]

        return FileListResponse(
            path=path,
            items=file_items
        ), None

    def read_file(self, sandbox_id: str, path: str) -> Tuple[Optional[FileContentResponse], Optional[ErrorResponse]]:
        """
        Read file content from sandbox.

        Args:
            sandbox_id: Sandbox ID
            path: File path

        Returns:
            Tuple of (response, error)
        """
        instance = self._get_instance(sandbox_id)
        if not instance:
            return None, ErrorResponse(error="Sandbox not found", detail=f"ID: {sandbox_id}")

        content = self.docker.read_file(sandbox_id, path)
        if content is None:
            return None, ErrorResponse(error="File not found", detail=f"Path: {path}")

        return FileContentResponse(
            path=path,
            content=content,
            size=len(content)
        ), None

    def write_file(
        self,
        sandbox_id: str,
        path: str,
        content: str
    ) -> Tuple[bool, Optional[ErrorResponse]]:
        """
        Write file to sandbox.

        Args:
            sandbox_id: Sandbox ID
            path: File path
            content: File content

        Returns:
            Tuple of (success, error)
        """
        instance = self._get_instance(sandbox_id)
        if not instance:
            return False, ErrorResponse(error="Sandbox not found", detail=f"ID: {sandbox_id}")

        if not self.docker.write_file(sandbox_id, path, content):
            return False, ErrorResponse(error="Write failed", detail=f"Path: {path}")

        return True, None

    def upload_workspace(
        self,
        sandbox_id: str,
        source_path: str,
        clear_first: bool = False
    ) -> Tuple[bool, Optional[ErrorResponse]]:
        """
        Upload local files to sandbox workspace.

        This is useful for:
        - Re-uploading modified files after failed execution
        - Syncing local changes to sandbox
        - Restoring workspace from local backup

        Args:
            sandbox_id: Sandbox ID
            source_path: Local directory or zip file path
            clear_first: If True, clear workspace before uploading

        Returns:
            Tuple of (success, error)
        """
        instance = self._get_instance(sandbox_id)
        if not instance:
            return False, ErrorResponse(error="Sandbox not found", detail=f"ID: {sandbox_id}")

        if not os.path.exists(source_path):
            return False, ErrorResponse(error="Source path not found", detail=f"Path: {source_path}")

        sandbox_dir = self.docker.get_sandbox_dir(sandbox_id)

        # Optionally clear workspace first
        if clear_first:
            for item in os.listdir(sandbox_dir):
                item_path = os.path.join(sandbox_dir, item)
                if os.path.isdir(item_path):
                    shutil.rmtree(item_path)
                else:
                    os.remove(item_path)

        # Upload workspace
        if not self.docker.upload_workspace(sandbox_id, source_path):
            return False, ErrorResponse(error="Upload failed", detail=f"Source: {source_path}")

        # Update file hash snapshot
        instance.file_hashes = self.docker.snapshot_files(sandbox_id)
        self._save_instance(instance)

        return True, None

    def sync_files(
        self,
        sandbox_id: str,
        files: Dict[str, str]
    ) -> Tuple[int, Optional[ErrorResponse]]:
        """
        Sync multiple files to sandbox.

        Args:
            sandbox_id: Sandbox ID
            files: Dict mapping sandbox_path -> local_path or content

        Returns:
            Tuple of (files_synced, error)
        """
        instance = self._get_instance(sandbox_id)
        if not instance:
            return 0, ErrorResponse(error="Sandbox not found", detail=f"ID: {sandbox_id}")

        synced = 0
        for sandbox_path, source in files.items():
            if os.path.exists(source):
                # It's a file path
                if self.docker.write_file(sandbox_id, sandbox_path, open(source, 'rb').read()):
                    synced += 1
            else:
                # It's content
                if self.docker.write_file(sandbox_id, sandbox_path, source):
                    synced += 1

        # Update snapshot
        if synced > 0:
            instance.file_hashes = self.docker.snapshot_files(sandbox_id)
            self._save_instance(instance)

        return synced, None

    def export_workspace(
        self,
        sandbox_id: str,
        path: str
    ) -> Tuple[Optional[ExportResponse], Optional[ErrorResponse]]:
        """
        Export files from sandbox as artifact.

        Args:
            sandbox_id: Sandbox ID
            path: Path to export

        Returns:
            Tuple of (response, error)
        """
        instance = self._get_instance(sandbox_id)
        if not instance:
            return None, ErrorResponse(error="Sandbox not found", detail=f"ID: {sandbox_id}")

        sandbox_dir = self.docker.get_sandbox_dir(sandbox_id)
        full_path = os.path.join(sandbox_dir, path)

        if not os.path.exists(full_path):
            return None, ErrorResponse(error="Path not found", detail=f"Path: {path}")

        # Create zip archive
        artifact_id = f"art_{uuid.uuid4().hex[:12]}"
        artifact_path = os.path.join(sandbox_dir, f"{artifact_id}.zip")

        try:
            with zipfile.ZipFile(artifact_path, 'w', zipfile.ZIP_DEFLATED) as zf:
                if os.path.isdir(full_path):
                    for root, dirs, files in os.walk(full_path):
                        for file in files:
                            file_path = os.path.join(root, file)
                            arcname = os.path.relpath(file_path, sandbox_dir)
                            zf.write(file_path, arcname)
                else:
                    zf.write(full_path, os.path.basename(full_path))

            file_size = os.path.getsize(artifact_path)

            return ExportResponse(
                artifact_id=artifact_id,
                path=path,
                size=file_size,
                download_url=f"/api/sandbox/artifacts/{sandbox_id}/{artifact_id}.zip"
            ), None

        except Exception as e:
            return None, ErrorResponse(error="Export failed", detail=str(e))

    def destroy_sandbox(
        self,
        sandbox_id: str,
        export_path: Optional[str] = None
    ) -> Tuple[Optional[DestroyResponse], Optional[ErrorResponse]]:
        """
        Destroy a sandbox.

        Args:
            sandbox_id: Sandbox ID
            export_path: Optional path to export before destroying

        Returns:
            Tuple of (response, error)
        """
        instance = self._get_instance(sandbox_id)
        if not instance:
            return None, ErrorResponse(error="Sandbox not found", detail=f"ID: {sandbox_id}")

        artifact_exported = False
        artifact_id = None

        # Export if requested
        if export_path:
            response, error = self.export_workspace(sandbox_id, export_path)
            if response:
                artifact_exported = True
                artifact_id = response.artifact_id

        # Get container
        container = self.docker.get_container_by_id(instance.container_id)

        # Cleanup
        self.docker.cleanup_sandbox(sandbox_id, container, keep_files=False)

        # Remove from tracking
        self._remove_instance(sandbox_id)
        logger.info(f"Sandbox {sandbox_id} destroyed")

        return DestroyResponse(
            sandbox_id=sandbox_id,
            status="destroyed",
            artifact_exported=artifact_exported,
            artifact_id=artifact_id
        ), None

    def list_sandboxes(self) -> List[Dict]:
        """List all running sandboxes."""
        # Reload from storage to get any recovered sandboxes
        self._mark_expired()

        result = []
        for sandbox_id in list(self._instances.keys()):
            instance = self._get_instance(sandbox_id)
            if not instance:
                continue

            container = self.docker.get_container_by_id(instance.container_id)
            status = container.status if container else "unknown"
            if instance.is_expired():
                status = "expired"

            result.append({
                "sandbox_id": sandbox_id,
                "template": instance.template_id,
                "status": status,
                "created_at": instance.created_at.isoformat(),
                "expires_at": instance.expires_at.isoformat()
            })
        return result

    def _parse_timeout(self, timeout_str: str) -> datetime:
        """Parse timeout string to datetime."""
        now = datetime.now()
        if timeout_str.endswith('m'):
            minutes = int(timeout_str[:-1])
            return now + timedelta(minutes=minutes)
        elif timeout_str.endswith('h'):
            hours = int(timeout_str[:-1])
            return now + timedelta(hours=hours)
        elif timeout_str.endswith('s'):
            seconds = int(timeout_str[:-1])
            return now + timedelta(seconds=seconds)
        else:
            # Default 30 minutes
            return now + timedelta(minutes=30)

    def _resolve_artifact(self, artifact_id: str) -> Optional[str]:
        """Resolve artifact ID to file path."""
        # Look in static directory
        static_root = os.path.join(os.path.dirname(os.path.dirname(__file__)), "static", "projects")

        for project_dir in os.listdir(static_root):
            artifacts_dir = os.path.join(static_root, project_dir, "artifacts")
            if os.path.exists(artifacts_dir):
                for artifact in os.listdir(artifacts_dir):
                    if artifact.startswith(artifact_id):
                        return os.path.join(artifacts_dir, artifact)

        return None
