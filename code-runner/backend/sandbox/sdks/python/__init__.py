"""
Sandbox SDK for Python.

A Python client for the Code Runner Sandbox Runtime API.

Example usage:
    from sandbox_sdks.python import SandboxClient

    async def main():
        async with SandboxClient() as client:
            # Create sandbox
            sandbox = await client.create_sandbox("python-basic")
            print(f"Created: {sandbox.id}")

            # Execute command
            result = await client.exec(sandbox.id, "python --version")
            print(f"Exit code: {result.exit_code}")

            # Destroy
            await client.destroy(sandbox.id)
"""
from __future__ import annotations

import asyncio
import httpx
from dataclasses import dataclass
from typing import Optional, List, Dict, Any
from contextlib import asynccontextmanager


# ============ Models ============

@dataclass
class Sandbox:
    """Represents a running sandbox."""
    id: str
    status: str
    workdir: str
    template: str
    created_at: str

    @classmethod
    def from_response(cls, data: Dict) -> "Sandbox":
        return cls(
            id=data["sandbox_id"],
            status=data["status"],
            workdir=data["paths"]["workspace"],
            template=data["runtime"]["resolved_from"].replace("template:", ""),
            created_at=data.get("created_at", "")
        )


@dataclass
class SandboxDetail:
    """Detailed sandbox information."""
    id: str
    status: str
    template: str
    workdir: str
    created_at: str
    expires_at: Optional[str] = None

    @classmethod
    def from_response(cls, data: Dict) -> "SandboxDetail":
        return cls(
            id=data["sandbox_id"],
            status=data["status"],
            template=data["template"],
            workdir=data["paths"]["workspace"],
            created_at=data["created_at"],
            expires_at=data.get("expires_at")
        )


@dataclass
class Environment:
    """Sandbox environment information."""
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
        """Check if command succeeded."""
        return self.exit_code == 0


@dataclass
class FileItem:
    """A file or directory in the sandbox."""
    name: str
    path: str
    is_dir: bool
    size: Optional[int] = None

    @classmethod
    def from_response(cls, data: Dict) -> "FileItem":
        return cls(
            name=data["name"],
            path=data["path"],
            is_dir=data["is_dir"],
            size=data.get("size")
        )


@dataclass
class FileContent:
    """File content response."""
    path: str
    content: str
    size: int

    @classmethod
    def from_response(cls, data: Dict) -> "FileContent":
        return cls(
            path=data["path"],
            content=data["content"],
            size=data["size"]
        )


@dataclass
class ExportResult:
    """Export result."""
    artifact_id: str
    path: str
    size: int
    download_url: str

    @classmethod
    def from_response(cls, data: Dict) -> "ExportResult":
        return cls(
            artifact_id=data["artifact_id"],
            path=data["path"],
            size=data["size"],
            download_url=data["download_url"]
        )


@dataclass
class Template:
    """Sandbox template definition."""
    id: str
    description: str
    capabilities: List[str]
    defaults: Dict[str, str]
    constraints: Dict[str, Any]

    @classmethod
    def from_response(cls, data: Dict) -> "Template":
        return cls(
            id=data["id"],
            description=data["description"],
            capabilities=data.get("capabilities", []),
            defaults=data.get("defaults", {}),
            constraints=data.get("constraints", {})
        )


# ============ Client ============

class SandboxClient:
    """Client for the Code Runner Sandbox Runtime API."""

    def __init__(
        self,
        base_url: str = "http://localhost:8002",
        timeout: float = 300.0
    ):
        """
        Initialize the sandbox client.

        Args:
            base_url: Base URL of the sandbox API server
            timeout: Default timeout for HTTP requests in seconds
        """
        self.base_url = base_url.rstrip("/")
        self.timeout = timeout
        self._client: Optional[httpx.AsyncClient] = None

    async def __aenter__(self):
        """Async context manager entry."""
        self._client = httpx.AsyncClient(
            base_url=self.base_url,
            timeout=self.timeout
        )
        return self

    async def __aexit__(self, exc_type, exc_val, exc_tb):
        """Async context manager exit."""
        await self._client.aclose()
        self._client = None

    @property
    def client(self) -> httpx.AsyncClient:
        """Get the HTTP client, raising if not initialized."""
        if self._client is None:
            raise RuntimeError(
                "Client not initialized. Use 'async with SandboxClient() as client:'"
            )
        return self._client

    # ============ Templates ============

    async def list_templates(self) -> List[Template]:
        """List all available templates."""
        resp = await self.client.get("/api/sandbox/templates")
        resp.raise_for_status()
        return [Template.from_response(t) for t in resp.json()["templates"]]

    async def get_template(self, template_id: str) -> Template:
        """Get a specific template."""
        resp = await self.client.get(f"/api/sandbox/templates/{template_id}")
        resp.raise_for_status()
        return Template.from_response(resp.json())

    # ============ Sandbox Lifecycle ============

    async def create_sandbox(
        self,
        template: str,
        workspace_files: Optional[str] = None
    ) -> Sandbox:
        """
        Create a new sandbox.

        Note: Resource limits are defined by the template. Heavy templates
        (python-data, jupyter-python) get 1GB memory, others get 256MB.

        Args:
            template: Template ID to use (e.g., "python-basic", "node-basic")
            workspace_files: Optional path to workspace files (local or artifact://)

        Returns:
            Created Sandbox instance
        """
        request_body = {"template": template}
        if workspace_files:
            request_body["workspace"] = {"files": workspace_files}

        resp = await self.client.post("/api/sandbox/sandboxes", json=request_body)
        resp.raise_for_status()
        return Sandbox.from_response(resp.json())

    async def get_sandbox(self, sandbox_id: str) -> SandboxDetail:
        """Get sandbox details."""
        resp = await self.client.get(f"/api/sandbox/sandboxes/{sandbox_id}")
        resp.raise_for_status()
        return SandboxDetail.from_response(resp.json())

    async def list_sandboxes(self) -> List[Sandbox]:
        """List all running sandboxes."""
        resp = await self.client.get("/api/sandbox/sandboxes")
        resp.raise_for_status()
        return [Sandbox.from_response(s) for s in resp.json()]

    async def destroy(
        self,
        sandbox_id: str,
        export_path: Optional[str] = None
    ) -> None:
        """
        Destroy a sandbox.

        Args:
            sandbox_id: Sandbox ID to destroy
            export_path: Optional path to export before destroying
        """
        params = {}
        if export_path:
            params["export"] = export_path

        resp = await self.client.delete(
            f"/api/sandbox/sandboxes/{sandbox_id}",
            params=params
        )
        resp.raise_for_status()

    # ============ Environment ============

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
        env: Optional[Dict[str, str]] = None,
        timeout: Optional[int] = None
    ) -> ExecResult:
        """
        Execute a command in the sandbox.

        Args:
            sandbox_id: Sandbox ID
            cmd: Command to execute (shell command)
            cwd: Working directory
            env: Environment variables
            timeout: Maximum execution time in seconds (None for no limit)

        Returns:
            ExecResult with exit code, stdout, stderr, etc.

        Raises:
            httpx.HTTPStatusError: If the command times out or fails
        """
        request_body: Dict[str, Any] = {"cmd": cmd}
        if cwd:
            request_body["cwd"] = cwd
        if env:
            request_body["env"] = env
        if timeout:
            request_body["timeout"] = timeout

        resp = await self.client.post(
            f"/api/sandbox/sandboxes/{sandbox_id}/exec",
            json=request_body
        )
        resp.raise_for_status()
        return ExecResult.from_response(resp.json())

    async def exec_and_check(
        self,
        sandbox_id: str,
        cmd: str,
        cwd: Optional[str] = None,
        timeout: Optional[int] = None
    ) -> ExecResult:
        """
        Execute a command and raise if it fails.

        Useful for scripts that expect commands to succeed.

        Args:
            sandbox_id: Sandbox ID
            cmd: Command to execute
            cwd: Working directory
            timeout: Maximum execution time in seconds

        Returns:
            ExecResult on success

        Raises:
            RuntimeError: If command exits with non-zero code
        """
        result = await self.exec(sandbox_id, cmd, cwd, timeout=timeout)
        if not result.success:
            raise RuntimeError(
                f"Command failed with exit code {result.exit_code}:\n{result.stderr}"
            )
        return result

    # ============ File Operations ============

    async def write_file(
        self,
        sandbox_id: str,
        path: str,
        content: str
    ) -> None:
        """Write file to sandbox."""
        resp = await self.client.post(
            f"/api/sandbox/sandboxes/{sandbox_id}/write",
            json={"path": path, "content": content}
        )
        resp.raise_for_status()

    async def read_file(self, sandbox_id: str, path: str) -> FileContent:
        """Read file from sandbox."""
        resp = await self.client.get(
            f"/api/sandbox/sandboxes/{sandbox_id}/file",
            params={"path": path}
        )
        resp.raise_for_status()
        return FileContent.from_response(resp.json())

    async def list_files(
        self,
        sandbox_id: str,
        path: str = "."
    ) -> List[FileItem]:
        """List files in sandbox."""
        resp = await self.client.get(
            f"/api/sandbox/sandboxes/{sandbox_id}/files",
            params={"path": path}
        )
        resp.raise_for_status()
        return [FileItem.from_response(item) for item in resp.json()["items"]]

    # ============ Workspace Operations ============

    async def export(
        self,
        sandbox_id: str,
        path: str = "."
    ) -> ExportResult:
        """
        Export files from sandbox as artifact.

        Args:
            sandbox_id: Sandbox ID
            path: Path to export (default: current directory)

        Returns:
            ExportResult with download URL
        """
        resp = await self.client.post(
            f"/api/sandbox/sandboxes/{sandbox_id}/export",
            json={"path": path, "as_artifact": True}
        )
        resp.raise_for_status()
        return ExportResult.from_response(resp.json())

    async def upload_workspace(
        self,
        sandbox_id: str,
        source_path: str,
        clear_first: bool = False
    ) -> None:
        """
        Upload local files to sandbox workspace.

        Args:
            sandbox_id: Sandbox ID
            source_path: Local directory or archive path
            clear_first: If True, clear workspace before uploading
        """
        resp = await self.client.post(
            f"/api/sandbox/sandboxes/{sandbox_id}/upload",
            params={"clear_first": str(clear_first).lower()},
            json={"source_path": source_path}
        )
        resp.raise_for_status()

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

    # ============ Convenience Methods ============

    async def run_script(
        self,
        sandbox_id: str,
        script: str,
        timeout: Optional[int] = None
    ) -> ExecResult:
        """
        Execute a multi-line script.

        The script will be written to a temporary file and executed.

        Args:
            sandbox_id: Sandbox ID
            script: Script content (bash)
            timeout: Maximum execution time in seconds

        Returns:
            ExecResult
        """
        script_path = "/tmp/script.sh"
        await self.write_file(sandbox_id, script_path, f"#!/bin/bash\nset -e\n{script}")
        return await self.exec_and_check(sandbox_id, f"bash {script_path}", timeout=timeout)


# ============ Synchronous Wrapper ============

class SyncSandboxClient:
    """Synchronous wrapper for SandboxClient."""

    def __init__(
        self,
        base_url: str = "http://localhost:8002",
        timeout: float = 300.0
    ):
        """
        Initialize the synchronous sandbox client.

        Note: This is a convenience wrapper. For production use,
        prefer the async SandboxClient.
        """
        import warnings
        warnings.warn(
            "SyncSandboxClient is deprecated. Use async SandboxClient instead.",
            DeprecationWarning,
            stacklevel=2
        )
        self.base_url = base_url
        self.timeout = timeout
        self._client: Optional[httpx.Client] = None

    def __enter__(self):
        """Context manager entry."""
        import httpx
        self._client = httpx.Client(
            base_url=self.base_url,
            timeout=self.timeout
        )
        return self

    def __exit__(self, exc_type, exc_val, exc_tb):
        """Context manager exit."""
        self._client.close()
        self._client = None

    def __getattr__(self, name: str):
        """Delegate attribute access to the sync client."""
        if self._client is None:
            raise RuntimeError("Client not initialized. Use context manager.")
        return getattr(self._client, name)


# ============ Example Usage ============

async def example():
    """Example of using the sandbox client."""
    async with SandboxClient() as client:
        # List templates
        templates = await client.list_templates()
        print("Available templates:")
        for t in templates:
            print(f"  - {t.id}: {t.description}")

        # Create sandbox
        print("\nCreating sandbox...")
        sandbox = await client.create_sandbox("python-basic")
        print(f"Created: {sandbox.id} at {sandbox.workdir}")

        # Execute command
        result = await client.exec(sandbox.id, "python --version")
        print(f"Python: {result.stdout.strip()}")

        # Write and run script
        await client.write_file(sandbox.id, "hello.py", 'print("Hello from sandbox!")')
        result = await client.exec_and_check(sandbox.id, "python hello.py")
        print(f"Script: {result.stdout.strip()}")

        # Export workspace
        export = await client.export(sandbox.id, ".")
        print(f"Exported: {export.download_url}")

        # Destroy
        await client.destroy(sandbox.id)
        print("Destroyed!")


if __name__ == "__main__":
    asyncio.run(example())
