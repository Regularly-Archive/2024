"""
Sandbox SDK client for Python.

Example usage:
    from sandbox_client import SandboxClient

    async def main():
        async with SandboxClient() as client:
            # Create sandbox
            sandbox = await client.create_sandbox("python-basic")
            print(f"Created: {sandbox.sandbox_id}")

            # Execute command
            result = await client.exec(sandbox.sandbox_id, "python --version")
            print(f"Exit code: {result.exit_code}")
            print(f"Output: {result.stdout}")

            # Destroy
            await client.destroy(sandbox.sandbox_id)
"""
import httpx
from typing import Optional, List, Dict, Any
from dataclasses import dataclass
from contextlib import asynccontextmanager

import os
import sys

# Add parent directory to path for imports
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from sandbox.models import (
    Template, SandboxStatus, SandboxCreateRequest, SandboxResponse,
    SandboxDetailResponse, EnvironmentResponse, ExecRequest, ExecResponse,
    FileListResponse, FileContentResponse, ExportResponse, DestroyResponse
)


@dataclass
class Sandbox:
    """Represents a running sandbox."""
    sandbox_id: str
    status: str
    workdir: str
    template: str

    @classmethod
    def from_response(cls, data: Dict) -> "Sandbox":
        return cls(
            sandbox_id=data["sandbox_id"],
            status=data["status"],
            workdir=data["paths"]["workspace"],
            template=data["runtime"]["resolved_from"].replace("template:", "")
        )


@dataclass
class Environment:
    """Represents sandbox environment information."""
    os: str
    arch: str
    capabilities: List[str]
    paths: Dict[str, str]

    @classmethod
    def from_response(cls, data: Dict) -> "Environment":
        return cls(
            os=data["os"],
            arch=data["arch"],
            capabilities=data["capabilities"],
            paths=data["paths"]
        )


@dataclass
class ExecResult:
    """Result of executing a command."""
    execution_id: str
    exit_code: int
    stdout: str
    stderr: str
    duration_ms: float
    files_changed: List[str]

    @classmethod
    def from_response(cls, data: Dict) -> "ExecResult":
        return cls(
            execution_id=data["execution_id"],
            exit_code=data["exit_code"],
            stdout=data["stdout"],
            stderr=data["stderr"],
            duration_ms=data["duration_ms"],
            files_changed=data.get("files_changed", [])
        )

    @property
    def success(self) -> bool:
        return self.exit_code == 0


class SandboxClient:
    """Client for the Sandbox Runtime API."""

    def __init__(self, base_url: str = "http://localhost:8002"):
        self.base_url = base_url
        self._client: Optional[httpx.AsyncClient] = None

    async def __aenter__(self):
        self._client = httpx.AsyncClient(base_url=self.base_url, timeout=300.0)
        return self

    async def __aexit__(self, exc_type, exc_val, exc_tb):
        await self._client.aclose()

    @property
    def client(self) -> httpx.AsyncClient:
        if self._client is None:
            raise RuntimeError("Client not initialized. Use async context manager.")
        return self._client

    # ============ Template Operations ============

    async def list_templates(self) -> List[Template]:
        """List all available templates."""
        resp = await self.client.get("/api/sandbox/templates")
        resp.raise_for_status()
        return [Template(**t) for t in resp.json()["templates"]]

    async def get_template(self, template_id: str) -> Template:
        """Get a specific template."""
        resp = await self.client.get(f"/api/sandbox/templates/{template_id}")
        resp.raise_for_status()
        return Template(**resp.json())

    # ============ Sandbox Lifecycle ============

    async def create_sandbox(
        self,
        template: str,
        workspace_files: Optional[str] = None,
        cpu: int = 2,
        memory: str = "4GiB",
        timeout: str = "30m"
    ) -> Sandbox:
        """
        Create a new sandbox.

        Args:
            template: Template ID to use
            workspace_files: Optional path to workspace files (local path or artifact URL)
            cpu: Number of CPUs
            memory: Memory limit
            timeout: Maximum execution time

        Returns:
            Sandbox instance
        """
        request_body = {
            "template": template,
            "resources": {
                "cpu": cpu,
                "memory": memory,
                "timeout": timeout
            }
        }

        if workspace_files:
            request_body["workspace"] = {
                "files": workspace_files
            }

        resp = await self.client.post("/api/sandbox/sandboxes", json=request_body)
        resp.raise_for_status()
        return Sandbox.from_response(resp.json())

    async def get_sandbox(self, sandbox_id: str) -> SandboxDetailResponse:
        """Get sandbox details."""
        resp = await self.client.get(f"/api/sandbox/sandboxes/{sandbox_id}")
        resp.raise_for_status()
        data = resp.json()
        return SandboxDetailResponse(**data)

    async def list_sandboxes(self) -> List[Dict]:
        """List all running sandboxes."""
        resp = await self.client.get("/api/sandbox/sandboxes")
        resp.raise_for_status()
        return resp.json()

    async def destroy(
        self,
        sandbox_id: str,
        export_path: Optional[str] = None
    ) -> DestroyResponse:
        """
        Destroy a sandbox.

        Args:
            sandbox_id: Sandbox ID
            export_path: Optional path to export before destroying

        Returns:
            DestroyResponse
        """
        params = {}
        if export_path:
            params["export"] = export_path

        resp = await self.client.delete(f"/api/sandbox/sandboxes/{sandbox_id}", params=params)
        resp.raise_for_status()
        return DestroyResponse(**resp.json())

    # ============ Environment Discovery ============

    async def get_environment(self, sandbox_id: str) -> Environment:
        """
        Get sandbox environment information.

        This is AI-friendly and tells you what capabilities are available.
        """
        resp = await self.client.get(f"/api/sandbox/sandboxes/{sandbox_id}/env")
        resp.raise_for_status()
        return Environment.from_response(resp.json())

    # ============ Execution ============

    async def exec(
        self,
        sandbox_id: str,
        cmd: str,
        cwd: Optional[str] = None,
        env: Optional[Dict[str, str]] = None
    ) -> ExecResult:
        """
        Execute a command in the sandbox.

        Args:
            sandbox_id: Sandbox ID
            cmd: Command to execute (shell command)
            cwd: Working directory
            env: Environment variables

        Returns:
            ExecResult with exit code, stdout, stderr, etc.
        """
        request_body = {"cmd": cmd}
        if cwd:
            request_body["cwd"] = cwd
        if env:
            request_body["env"] = env

        resp = await self.client.post(
            f"/api/sandbox/sandboxes/{sandbox_id}/exec",
            json=request_body
        )
        resp.raise_for_status()
        return ExecResult.from_response(resp.json())

    # ============ File Operations ============

    async def list_files(self, sandbox_id: str, path: str = ".") -> FileListResponse:
        """List files in sandbox."""
        resp = await self.client.get(
            f"/api/sandbox/sandboxes/{sandbox_id}/files",
            params={"path": path}
        )
        resp.raise_for_status()
        return FileListResponse(**resp.json())

    async def read_file(self, sandbox_id: str, path: str) -> FileContentResponse:
        """Read file from sandbox."""
        resp = await self.client.get(
            f"/api/sandbox/sandboxes/{sandbox_id}/file",
            params={"path": path}
        )
        resp.raise_for_status()
        return FileContentResponse(**resp.json())

    async def write_file(self, sandbox_id: str, path: str, content: str) -> bool:
        """Write file to sandbox."""
        resp = await self.client.post(
            f"/api/sandbox/sandboxes/{sandbox_id}/write",
            json={"path": path, "content": content}
        )
        resp.raise_for_status()
        return resp.json().get("status") == "ok"

    async def export(
        self,
        sandbox_id: str,
        path: str = "."
    ) -> ExportResponse:
        """Export files from sandbox as artifact."""
        resp = await self.client.post(
            f"/api/sandbox/sandboxes/{sandbox_id}/export",
            json={"path": path, "as_artifact": True}
        )
        resp.raise_for_status()
        return ExportResponse(**resp.json())

    # ============ Sync/Upload Methods ============

    async def upload_workspace(
        self,
        sandbox_id: str,
        source_path: str,
        clear_first: bool = False
    ) -> bool:
        """
        Upload local files to sandbox workspace.

        Useful for re-uploading modified files after failed execution.

        Args:
            sandbox_id: Sandbox ID
            source_path: Local directory or zip file path
            clear_first: If True, clear workspace before uploading

        Returns:
            True if successful
        """
        resp = await self.client.post(
            f"/api/sandbox/sandboxes/{sandbox_id}/upload",
            params={"clear_first": str(clear_first).lower()},
            json={"source_path": source_path}
        )
        resp.raise_for_status()
        return resp.json().get("status") == "ok"

    async def sync_files(
        self,
        sandbox_id: str,
        files: Dict[str, str]
    ) -> int:
        """
        Sync multiple files to sandbox.

        Args:
            sandbox_id: Sandbox ID
            files: Dict mapping sandbox_path -> local_path or content

        Returns:
            Number of files synced
        """
        resp = await self.client.post(
            f"/api/sandbox/sandboxes/{sandbox_id}/sync",
            json=files
        )
        resp.raise_for_status()
        return resp.json().get("synced", 0)

    # ============ Workflow Convenience Methods ============

    async def run_with_retry(
        self,
        sandbox_id: str,
        cmd: str,
        local_files: Optional[Dict[str, str]] = None,
        max_retries: int = 3
    ) -> ExecResult:
        """
        Execute a command with automatic file sync on failure.

        This is the main workflow method for AI agents.

        Args:
            sandbox_id: Sandbox ID
            cmd: Command to execute
            local_files: Optional dict of files to sync on failure
            max_retries: Maximum retries on failure

        Returns:
            ExecResult
        """
        for attempt in range(max_retries):
            result = await self.exec(sandbox_id, cmd)

            if result.success:
                return result

            # Command failed, try to sync files if provided
            if local_files and attempt < max_retries - 1:
                print(f"Command failed (attempt {attempt + 1}/{max_retries}), syncing files...")
                synced = await self.sync_files(sandbox_id, local_files)
                print(f"Synced {synced} files, retrying...")

        return result

    # ============ Convenience Methods ============

    async def exec_and_check(
        self,
        sandbox_id: str,
        cmd: str,
        cwd: Optional[str] = None
    ) -> ExecResult:
        """
        Execute a command and raise if it fails.

        Useful for scripts that expect commands to succeed.
        """
        result = await self.exec(sandbox_id, cmd, cwd)
        if not result.success:
            raise RuntimeError(
                f"Command failed with exit code {result.exit_code}:\n{result.stderr}"
            )
        return result

    async def exec_script(self, sandbox_id: str, script: str) -> ExecResult:
        """
        Execute a multi-line script.

        The script will be written to a temporary file and executed.
        """
        # Write script to a file
        script_path = "/tmp/script.sh"
        await self.write_file(sandbox_id, script_path, f"#!/bin/bash\nset -e\n{script}")

        # Make executable and run
        return await self.exec_and_check(sandbox_id, f"bash {script_path}")


# ============ Synchronous Wrapper ============

class SyncSandboxClient(SandboxClient):
    """Synchronous wrapper for SandboxClient."""

    def __init__(self, *args, **kwargs):
        import warnings
        warnings.warn(
            "SyncSandboxClient is deprecated. Use async with/asyncio instead.",
            DeprecationWarning
        )
        super().__init__(*args, **kwargs)
        self._sync_client: Optional[httpx.Client] = None

    def __enter__(self):
        self._sync_client = httpx.Client(base_url=self.base_url, timeout=300.0)
        self._client = self._sync_client  # type: ignore
        return self

    def __exit__(self, exc_type, exc_val, exc_tb):
        self._sync_client.close()
        self._sync_client = None
        self._client = None  # type: ignore


# ============ Example Usage ============

async def example_usage():
    """Example of using the sandbox client."""
    async with SandboxClient() as client:
        # 1. List templates
        templates = await client.list_templates()
        print("Available templates:")
        for t in templates:
            print(f"  - {t.id}: {t.description}")
            print(f"    Capabilities: {', '.join(t.capabilities[:5])}")

        # 2. Create a Python sandbox
        print("\nCreating sandbox...")
        sandbox = await client.create_sandbox("python-basic")
        print(f"Created: {sandbox.sandbox_id} at {sandbox.workdir}")

        # 3. Check environment
        print("\nChecking environment...")
        env = await client.get_environment(sandbox.sandbox_id)
        print(f"OS: {env.os}/{env.arch}")
        print(f"Capabilities: {env.capabilities}")

        # 4. Execute commands
        print("\nExecuting commands...")

        result = await client.exec(sandbox.sandbox_id, "python --version")
        print(f"Python version: {result.stdout.strip()}")

        # Run a Python script
        await client.write_file(
            sandbox.sandbox_id,
            "hello.py",
            'print("Hello from sandbox!")'
        )

        result = await client.exec(sandbox.sandbox_id, "python hello.py")
        print(f"Script output: {result.stdout.strip()}")

        # 5. List files
        print("\nListing files...")
        files = await client.list_files(sandbox.sandbox_id)
        for f in files.items:
            print(f"  {'[DIR]' if f.is_dir else '[FILE]'} {f.name}")

        # 6. Export results
        print("\nExporting workspace...")
        export = await client.export(sandbox.sandbox_id, ".")
        print(f"Exported to: {export.download_url}")

        # 7. Destroy
        print("\nDestroying sandbox...")
        await client.destroy(sandbox.sandbox_id)
        print("Done!")


if __name__ == "__main__":
    import asyncio
    asyncio.run(example_usage())
