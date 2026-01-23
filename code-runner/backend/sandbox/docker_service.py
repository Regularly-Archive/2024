"""
Docker service for the sandbox runtime.

Provides container lifecycle management and command execution.
"""
import os
import time
import docker
import shutil
import uuid
import hashlib
import shlex
from datetime import datetime
from typing import Optional, Dict, List, Tuple, Generator, Any
from services.logger import get_logger

logger = get_logger(__name__)


class ExecutionTimeout(Exception):
    """Raised when command execution exceeds timeout."""
    pass


class SandboxDockerClient:
    """Docker client for sandbox operations."""

    def __init__(self):
        self.client = docker.from_env()
        self.logger = logger
        self.cache_root = os.path.join(os.path.expanduser("~"), ".cache", "code-runner", "sandboxes")

    def _get_sandbox_dir(self, sandbox_id: str) -> str:
        """Get the sandbox working directory."""
        return os.path.join(self.cache_root, sandbox_id)

    def _ensure_sandbox_dir(self, sandbox_id: str) -> str:
        """Ensure sandbox directory exists."""
        sandbox_dir = self._get_sandbox_dir(sandbox_id)
        os.makedirs(sandbox_dir, exist_ok=True)
        return sandbox_dir

    def get_container_by_id(self, container_id: str) -> Optional[docker.models.containers.Container]:
        """Get a container by its full ID."""
        try:
            return self.client.containers.get(container_id)
        except docker.errors.NotFound:
            return None

    def create_container(
        self,
        sandbox_id: str,
        image_name: str,
        workdir: str = "/workspace",
        envs: Optional[Dict[str, str]] = None,
        resources: Optional[Dict[str, Any]] = None
    ) -> docker.models.containers.Container:
        """
        Create a new container for a sandbox.

        Args:
            sandbox_id: Unique sandbox identifier
            image_name: Docker image to use
            workdir: Working directory in the container
            envs: Environment variables
            resources: Resource limits (memory, cpu, pids)

        Returns:
            The created container
        """
        sandbox_dir = self._ensure_sandbox_dir(sandbox_id)

        default_envs = {
            'LANG': 'en_US.UTF-8',
            'LC_ALL': 'en_US.UTF-8',
            'HOME': '/home/sandbox',
            'SANDBOX_ID': sandbox_id,
        }
        merged_envs = {**default_envs, **(envs or {})}

        # Create logs directory
        logs_dir = os.path.join(sandbox_dir, "logs")
        os.makedirs(logs_dir, exist_ok=True)

        self.logger.info("Creating container from image %s for sandbox %s", image_name, sandbox_id)

        # Build container kwargs
        container_kwargs = dict(
            image=image_name,
            command="sleep infinity",
            volumes={
                os.path.abspath(sandbox_dir): {'bind': workdir, 'mode': 'rw'},
            },
            tty=True,
            detach=True,
            environment=merged_envs,
            labels={
                "sandbox_id": sandbox_id,
                "type": "sandbox",
            }
        )

        # Apply resource limits if provided
        if resources:
            container_kwargs["mem_limit"] = resources.get("memory", "256m")
            container_kwargs["cpu_period"] = 100000
            container_kwargs["cpu_quota"] = int(resources.get("cpu", 0.5) * 100000)
            container_kwargs["pids_limit"] = resources.get("pids", 100)
            self.logger.info(
                "Applying resource limits: mem=%s, cpu=%.2f, pids=%d",
                container_kwargs["mem_limit"],
                resources.get("cpu", 0.5),
                container_kwargs["pids_limit"]
            )

        container = self.client.containers.run(**container_kwargs)

        self.logger.info("Container %s created for sandbox %s", container.short_id, sandbox_id)
        return container

    def exec_command(
        self,
        container: docker.models.containers.Container,
        cmd: str,
        user: str = "sandbox",
        workdir: str = "/workspace",
        timeout: Optional[int] = None
    ) -> Tuple[int, str, str]:
        """
        Execute a command and wait for completion.

        Args:
            container: The container to execute in
            cmd: Command to execute
            user: User to run as
            workdir: Working directory
            timeout: Maximum execution time in seconds (None for no limit)

        Returns:
            Tuple of (exit_code, stdout, stderr)

        Raises:
            ExecutionTimeout: If execution exceeds timeout
        """
        # Use sh -c with proper quoting for shell execution
        # The command is passed as a single string to be executed by shell
        wrapped_cmd = f"sh -c {shlex.quote(cmd)}"

        stdout_chunks: List[str] = []
        stderr_chunks: List[str] = []
        exit_code_val = 0

        try:
            for stream, content in self.run_command_as_stream(
                container, wrapped_cmd, user, workdir, timeout
            ):
                if stream == "stdout":
                    stdout_chunks.append(content)
                elif stream == "stderr":
                    stderr_chunks.append(content)
                elif stream == "exit":
                    exit_code_val = int(content)
        except ExecutionTimeout:
            raise

        stdout = '\n'.join(stdout_chunks)
        stderr = '\n'.join(stderr_chunks)

        return exit_code_val, stdout, stderr

    def run_command_as_stream(
        self,
        container: docker.models.containers.Container,
        cmd: str,
        user: str = "sandbox",
        workdir: str = "/workspace",
        timeout: Optional[int] = None
    ) -> Generator[Tuple[str, str], None, None]:
        """
        Execute a command with streaming output and optional timeout.

        Args:
            container: The container to execute in
            cmd: Command to execute
            user: User to run as
            workdir: Working directory
            timeout: Maximum execution time in seconds (None for no limit)

        Yields:
            Tuples of (stream_type, content) where stream_type is 'stdout', 'stderr', or 'exit'

        Raises:
            ExecutionTimeout: If execution exceeds timeout
        """
        start_time = time.time()

        try:
            exec_id = self.client.api.exec_create(
                container.id,
                cmd=cmd,
                user=user,
                workdir=workdir,
            )["Id"]

            output = self.client.api.exec_start(
                exec_id,
                stream=True,
                demux=True
            )

            for stdout, stderr in output:
                # Check timeout
                if timeout is not None and (time.time() - start_time) > timeout:
                    # Try to kill the exec
                    try:
                        self.client.api.exec_kill(container.id, exec_id)
                    except Exception:
                        pass
                    raise ExecutionTimeout(f"Command execution exceeded {timeout}s timeout")

                if stdout:
                    yield "stdout", stdout.decode(errors='ignore')
                if stderr:
                    yield "stderr", stderr.decode(errors='ignore')

            inspect = self.client.api.exec_inspect(exec_id)
            yield "exit", str(inspect["ExitCode"])

        except docker.errors.APIError as e:
            self.logger.error("Docker API error during exec: %s", e)
            yield "stderr", str(e)
            yield "exit", "1"

    def inspect_container(self, container: docker.models.containers.Container) -> Dict:
        """Get container inspection data."""
        return self.client.api.inspect_container(container.id)

    def get_container_status(self, container: docker.models.containers.Container) -> str:
        """Get container status."""
        return container.status

    def stop_container(
        self,
        container: docker.models.containers.Container,
        timeout: int = 10
    ) -> None:
        """Stop a container."""
        self.logger.info("Stopping container %s", container.short_id)
        container.stop(timeout=timeout)

    def remove_container(
        self,
        container: docker.models.containers.Container,
        force: bool = True
    ) -> None:
        """Remove a container."""
        self.logger.info("Removing container %s", container.short_id)
        container.remove(force=force)

    def cleanup_sandbox(
        self,
        sandbox_id: str,
        container: Optional[docker.models.containers.Container],
        keep_files: bool = False
    ) -> None:
        """
        Complete cleanup of a sandbox.

        Args:
            sandbox_id: Sandbox identifier
            container: Container to clean up (None if already removed)
            keep_files: Whether to keep sandbox files
        """
        self.logger.info("Cleaning up sandbox %s", sandbox_id)

        if container:
            try:
                self.stop_container(container)
                self.remove_container(container)
            except docker.errors.APIError as e:
                self.logger.warning("Error during container cleanup: %s", e)

        if not keep_files:
            sandbox_dir = self._get_sandbox_dir(sandbox_id)
            if os.path.exists(sandbox_dir):
                try:
                    shutil.rmtree(sandbox_dir)
                    self.logger.info("Removed sandbox directory %s", sandbox_dir)
                except OSError as e:
                    self.logger.warning("Error removing sandbox directory: %s", e)

    def get_sandbox_dir(self, sandbox_id: str) -> str:
        """Get the sandbox working directory."""
        return self._get_sandbox_dir(sandbox_id)

    def list_sandboxes(self) -> List[Dict]:
        """List all running sandboxes."""
        containers = self.client.containers.list(
            filters={
                "label": "type=sandbox",
                "status": "running"
            }
        )

        result = []
        for container in containers:
            sandbox_id = container.labels.get("sandbox_id", "unknown")
            result.append({
                "sandbox_id": sandbox_id,
                "container_id": container.id,
                "short_id": container.short_id,
                "status": container.status,
                "image": container.image.tags[0] if container.image.tags else container.image.short_id,
                "created": container.attrs.get("Created", "unknown")
            })

        return result

    def get_file_hash(self, sandbox_id: str, path: str) -> Optional[str]:
        """
        Get MD5 hash of a file.

        Args:
            sandbox_id: Sandbox identifier
            path: Path relative to sandbox directory

        Returns:
            MD5 hash string or None if file doesn't exist
        """
        full_path = os.path.join(self._get_sandbox_dir(sandbox_id), path)
        if not os.path.exists(full_path):
            return None

        try:
            with open(full_path, 'rb') as f:
                return hashlib.md5(f.read()).hexdigest()
        except OSError:
            return None

    def list_directory(self, sandbox_id: str, path: str = ".") -> List[Dict]:
        """
        List directory contents.

        Args:
            sandbox_id: Sandbox identifier
            path: Path relative to sandbox directory

        Returns:
            List of file/directory info dicts
        """
        full_path = os.path.join(self._get_sandbox_dir(sandbox_id), path)
        if not os.path.exists(full_path) or not os.path.isdir(full_path):
            return []

        result = []
        for item in os.listdir(full_path):
            item_path = os.path.join(full_path, item)
            stat = os.stat(item_path)
            result.append({
                "name": item,
                "path": os.path.join(path, item).replace("\\", "/"),
                "is_dir": os.path.isdir(item_path),
                "size": stat.st_size if os.path.isfile(item_path) else None,
                "mtime": stat.st_mtime
            })

        return sorted(result, key=lambda x: (not x["is_dir"], x["name"]))

    def read_file(self, sandbox_id: str, path: str) -> Optional[str]:
        """
        Read file content.

        Args:
            sandbox_id: Sandbox identifier
            path: Path relative to sandbox directory

        Returns:
            File content or None if file doesn't exist
        """
        full_path = os.path.join(self._get_sandbox_dir(sandbox_id), path)
        if not os.path.exists(full_path):
            return None

        try:
            with open(full_path, 'r', encoding='utf-8') as f:
                return f.read()
        except OSError:
            return None

    def write_file(self, sandbox_id: str, path: str, content) -> bool:
        """
        Write file content.

        Args:
            sandbox_id: Sandbox identifier
            path: Path relative to sandbox directory
            content: File content (str or bytes)

        Returns:
            True if successful
        """
        sandbox_dir = self._get_sandbox_dir(sandbox_id)
        full_path = os.path.join(sandbox_dir, path)

        try:
            os.makedirs(os.path.dirname(full_path), exist_ok=True)
            mode = 'wb' if isinstance(content, bytes) else 'w'
            encoding = None if isinstance(content, bytes) else 'utf-8'
            with open(full_path, mode, encoding=encoding) as f:
                f.write(content)
            return True
        except OSError:
            return False

    def upload_workspace(self, sandbox_id: str, source_path: str) -> bool:
        """
        Upload workspace files to sandbox.

        Args:
            sandbox_id: Sandbox identifier
            source_path: Source directory or archive path

        Returns:
            True if successful
        """
        sandbox_dir = self._get_sandbox_dir(sandbox_id)

        try:
            if os.path.isdir(source_path):
                # Copy directory
                for item in os.listdir(source_path):
                    src = os.path.join(source_path, item)
                    dst = os.path.join(sandbox_dir, item)
                    if os.path.isdir(src):
                        shutil.copytree(src, dst)
                    else:
                        shutil.copy2(src, dst)
            elif source_path.endswith(('.zip', '.tar', '.tar.gz', '.tar.xz')):
                # Extract archive
                import tarfile
                import zipfile

                if source_path.endswith('.zip'):
                    with zipfile.ZipFile(source_path, 'r') as zf:
                        zf.extractall(sandbox_dir)
                else:
                    with tarfile.open(source_path, 'r:*') as tf:
                        tf.extractall(sandbox_dir)
            else:
                # Single file
                shutil.copy2(source_path, sandbox_dir)

            return True
        except Exception as e:
            self.logger.error("Error uploading workspace: %s", e)
            return False

    def scan_files_changed(self, sandbox_id: str, previous_hashes: Dict[str, str]) -> List[str]:
        """
        Scan for changed files.

        Args:
            sandbox_id: Sandbox identifier
            previous_hashes: Previous file hashes

        Returns:
            List of changed file paths
        """
        sandbox_dir = self._get_sandbox_dir(sandbox_id)
        changed = []

        for root, dirs, files in os.walk(sandbox_dir):
            rel_root = os.path.relpath(root, sandbox_dir)

            for filename in files:
                rel_path = os.path.join(rel_root, filename).replace("\\", "/")
                if rel_path.startswith('.'):
                    continue

                current_hash = self.get_file_hash(sandbox_id, rel_path)
                if current_hash and previous_hashes.get(rel_path) != current_hash:
                    changed.append(rel_path)

        return changed

    def snapshot_files(self, sandbox_id: str) -> Dict[str, str]:
        """
        Create a snapshot of file hashes.

        Args:
            sandbox_id: Sandbox identifier

        Returns:
            Dict mapping file paths to hashes
        """
        sandbox_dir = self._get_sandbox_dir(sandbox_id)
        hashes = {}

        for root, dirs, files in os.walk(sandbox_dir):
            for filename in files:
                rel_path = os.path.join(os.path.relpath(root, sandbox_dir), filename).replace("\\", "/")
                if rel_path.startswith('.'):
                    continue

                file_hash = self.get_file_hash(sandbox_id, rel_path)
                if file_hash:
                    hashes[rel_path] = file_hash

        return hashes
