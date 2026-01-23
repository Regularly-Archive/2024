"""
Unit tests for the Python Sandbox SDK.
"""
import pytest
import pytest_asyncio
from unittest.mock import AsyncMock, MagicMock, patch

from sandbox_sdks.python import (
    SandboxClient,
    Sandbox,
    SandboxDetail,
    Environment,
    ExecResult,
    FileItem,
    FileContent,
    ExportResult,
    Template,
)


class TestModels:
    """Test model serialization."""

    def test_sandbox_from_response(self):
        """Test Sandbox model from API response."""
        data = {
            "sandbox_id": "sbx_test123",
            "status": "running",
            "paths": {"workspace": "/workspace"},
            "runtime": {"image": "python:3.11", "resolved_from": "template:python-basic"},
            "created_at": "2024-01-01T00:00:00"
        }
        sandbox = Sandbox.from_response(data)

        assert sandbox.id == "sbx_test123"
        assert sandbox.status == "running"
        assert sandbox.workdir == "/workspace"
        assert sandbox.template == "python-basic"
        assert sandbox.created_at == "2024-01-01T00:00:00"

    def test_sandbox_detail_from_response(self):
        """Test SandboxDetail model from API response."""
        data = {
            "sandbox_id": "sbx_test123",
            "template": "python-basic",
            "status": "running",
            "paths": {"workspace": "/workspace"},
            "runtime": {"image": "python:3.11", "resolved_from": "template:python-basic"},
            "created_at": "2024-01-01T00:00:00",
            "expires_at": "2024-01-01T01:00:00"
        }
        detail = SandboxDetail.from_response(data)

        assert detail.id == "sbx_test123"
        assert detail.template == "python-basic"
        assert detail.expires_at == "2024-01-01T01:00:00"

    def test_environment_from_response(self):
        """Test Environment model from API response."""
        data = {
            "os": "linux",
            "arch": "amd64",
            "capabilities": ["bash", "python@3.11", "pip"],
            "paths": {"workspace": "/workspace"}
        }
        env = Environment.from_response(data)

        assert env.os == "linux"
        assert env.arch == "amd64"
        assert "python@3.11" in env.capabilities

    def test_exec_result_success(self):
        """Test ExecResult success property."""
        result = ExecResult(
            execution_id="exec_123",
            exit_code=0,
            stdout="Hello",
            stderr="",
            duration_ms=100.0,
            files_changed=[]
        )
        assert result.success is True

        result_fail = ExecResult(
            execution_id="exec_123",
            exit_code=1,
            stdout="",
            stderr="Error",
            duration_ms=100.0,
            files_changed=[]
        )
        assert result_fail.success is False

    def test_file_item_from_response(self):
        """Test FileItem model from API response."""
        data = {
            "name": "test.py",
            "path": "/workspace/test.py",
            "is_dir": False,
            "size": 1024
        }
        item = FileItem.from_response(data)

        assert item.name == "test.py"
        assert item.is_dir is False
        assert item.size == 1024

    def test_template_from_response(self):
        """Test Template model from API response."""
        data = {
            "id": "python-basic",
            "description": "Python runtime",
            "capabilities": ["bash", "python@3.11"],
            "defaults": {"workdir": "/workspace"},
            "constraints": {"max_exec_time": "30m"}
        }
        template = Template.from_response(data)

        assert template.id == "python-basic"
        assert "python@3.11" in template.capabilities
        assert template.defaults["workdir"] == "/workspace"


class TestSandboxClient:
    """Test SandboxClient HTTP operations with mocks."""

    @pytest_asyncio.fixture
    async def mock_client(self):
        """Create a mock HTTP client."""
        with patch("sandbox_sdks.python.httpx.AsyncClient") as mock:
            client = AsyncMock()
            mock.return_value = client
            yield client

    @pytest.mark.asyncio
    async def test_list_templates(self, mock_client):
        """Test listing templates."""
        mock_client.get.return_value = AsyncMock()
        mock_client.get.return_value.raise_for_status = MagicMock()
        mock_client.get.return_value.json.return_value = {
            "templates": [
                {"id": "python-basic", "description": "Python", "capabilities": [], "defaults": {}, "constraints": {}}
            ]
        }

        async with SandboxClient() as client:
            client._client = mock_client  # Inject mock
            templates = await client.list_templates()

        assert len(templates) == 1
        assert templates[0].id == "python-basic"

    @pytest.mark.asyncio
    async def test_create_sandbox(self, mock_client):
        """Test creating a sandbox."""
        mock_client.post.return_value = AsyncMock()
        mock_client.post.return_value.raise_for_status = MagicMock()
        mock_client.post.return_value.json.return_value = {
            "sandbox_id": "sbx_new123",
            "status": "running",
            "paths": {"workspace": "/workspace"},
            "runtime": {"image": "python:3.11", "resolved_from": "template:python-basic"},
            "created_at": "2024-01-01T00:00:00"
        }

        async with SandboxClient() as client:
            client._client = mock_client
            sandbox = await client.create_sandbox("python-basic")

        assert sandbox.id == "sbx_new123"
        assert sandbox.template == "python-basic"

    @pytest.mark.asyncio
    async def test_exec_with_timeout(self, mock_client):
        """Test executing command with timeout."""
        mock_client.post.return_value = AsyncMock()
        mock_client.post.return_value.raise_for_status = MagicMock()
        mock_client.post.return_value.json.return_value = {
            "execution_id": "exec_123",
            "exit_code": 0,
            "stdout": "Hello",
            "stderr": "",
            "duration_ms": 100.0,
            "files_changed": []
        }

        async with SandboxClient() as client:
            client._client = mock_client
            result = await client.exec("sbx_test", "echo hello", timeout=30)

        assert result.exit_code == 0
        assert result.stdout == "Hello"

        # Verify timeout was sent in request
        call_args = mock_client.post.call_args
        body = call_args.kwargs.get("json") or call_args[1].get("json")
        assert body.get("timeout") == 30

    @pytest.mark.asyncio
    async def test_write_file(self, mock_client):
        """Test writing a file."""
        mock_client.post.return_value = AsyncMock()
        mock_client.post.return_value.raise_for_status = MagicMock()

        async with SandboxClient() as client:
            client._client = mock_client
            await client.write_file("sbx_test", "test.py", "print('hello')")

        # Verify the request was made correctly
        call_args = mock_client.post.call_args
        body = call_args.kwargs.get("json") or call_args[1].get("json")
        assert body.get("path") == "test.py"
        assert body.get("content") == "print('hello')"

    @pytest.mark.asyncio
    async def test_list_files(self, mock_client):
        """Test listing files."""
        mock_client.get.return_value = AsyncMock()
        mock_client.get.return_value.raise_for_status = MagicMock()
        mock_client.get.return_value.json.return_value = {
            "items": [
                {"name": "file1.txt", "path": "/workspace/file1.txt", "is_dir": False, "size": 100},
                {"name": "folder", "path": "/workspace/folder", "is_dir": True}
            ]
        }

        async with SandboxClient() as client:
            client._client = mock_client
            files = await client.list_files("sbx_test", "/workspace")

        assert len(files) == 2
        assert files[0].name == "file1.txt"
        assert files[1].is_dir is True

    @pytest.mark.asyncio
    async test_exec_and_check_success(self, mock_client):
        """Test exec_and_check on success."""
        mock_client.post.return_value = AsyncMock()
        mock_client.post.return_value.raise_for_status = MagicMock()
        mock_client.post.return_value.json.return_value = {
            "execution_id": "exec_123",
            "exit_code": 0,
            "stdout": "OK",
            "stderr": "",
            "duration_ms": 100.0,
            "files_changed": []
        }

        async with SandboxClient() as client:
            client._client = mock_client
            result = await client.exec_and_check("sbx_test", "echo OK")

        assert result.exit_code == 0

    @pytest.mark.asyncio
    async def test_exec_and_check_failure(self, mock_client):
        """Test exec_and_check raises on failure."""
        mock_client.post.return_value = AsyncMock()
        mock_client.post.return_value.raise_for_status = MagicMock()
        mock_client.post.return_value.json.return_value = {
            "execution_id": "exec_123",
            "exit_code": 1,
            "stdout": "",
            "stderr": "Command failed",
            "duration_ms": 100.0,
            "files_changed": []
        }

        async with SandboxClient() as client:
            client._client = mock_client
            with pytest.raises(RuntimeError, match="Command failed"):
                await client.exec_and_check("sbx_test", "failing_command")


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
