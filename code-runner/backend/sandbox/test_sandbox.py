"""
Integration tests for the sandbox module.

These tests create real sandboxes and execute commands.
Requires Docker to be running.
"""
import sys
import os
import time
import tempfile
import zipfile

# Add parent directory to path
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))


def test_imports():
    """Test that all modules can be imported."""
    from sandbox import (
        SandboxService,
        SandboxDockerClient,
        SandboxStorage,
        SandboxRepository,
        list_templates,
        get_template,
        resolve_image,
    )
    print("[OK] All imports successful")


def test_templates():
    """Test template operations."""
    from sandbox import list_templates, get_template, resolve_image, TEMPLATES

    templates = list_templates()
    assert len(templates) > 0, "Should have at least one template"
    print(f"[OK] Found {len(templates)} templates")

    # Test getting a specific template
    template = get_template("python-basic")
    assert template.id == "python-basic"
    assert any("python" in c for c in template.capabilities)
    print(f"[OK] Template 'python-basic' has capabilities: {template.capabilities}")

    # Test resolving image
    image = resolve_image("python-basic")
    assert image == "code_runner/python3"
    print(f"[OK] Resolved python-basic to {image}")


def test_storage():
    """Test storage operations."""
    from sandbox import SandboxStorage, SandboxRepository

    # Use a temporary file for testing
    import tempfile
    with tempfile.NamedTemporaryFile(suffix=".db", delete=False) as f:
        db_path = f.name

    try:
        storage = SandboxStorage(db_path)
        print("[OK] Storage initialized")

        # Check stats
        stats = storage.get_stats()
        print(f"[OK] Storage stats: {stats}")
    finally:
        os.unlink(db_path)
        print("[OK] Storage test completed")


def test_sandbox_lifecycle():
    """Test creating and destroying a sandbox."""
    from sandbox import SandboxService, SandboxCreateRequest

    service = SandboxService()

    # List templates
    templates = service.list_templates()
    assert len(templates) > 0
    print(f"[OK] Found {len(templates)} templates")

    # Create a sandbox
    request = SandboxCreateRequest(template="python-basic")
    response, error = service.create_sandbox(request)
    assert error is None, f"Failed to create sandbox: {error}"
    assert response is not None
    sandbox_id = response.sandbox_id
    print(f"[OK] Created sandbox: {sandbox_id}")

    # Get sandbox details
    detail, error = service.get_sandbox(sandbox_id)
    assert error is None
    assert detail.sandbox_id == sandbox_id
    print(f"[OK] Sandbox details: template={detail.template}, status={detail.status}")

    # List sandboxes
    sandboxes = service.list_sandboxes()
    assert any(s["sandbox_id"] == sandbox_id for s in sandboxes)
    print(f"[OK] Listed {len(sandboxes)} sandbox(es)")

    # Destroy sandbox
    destroy_resp, error = service.destroy_sandbox(sandbox_id)
    assert error is None
    assert destroy_resp.status == "destroyed"
    print(f"[OK] Destroyed sandbox: {sandbox_id}")


def test_bash_commands():
    """Test executing bash commands."""
    from sandbox import SandboxService, SandboxCreateRequest

    service = SandboxService()

    # Create a sandbox
    request = SandboxCreateRequest(template="linux-basic")
    response, error = service.create_sandbox(request)
    assert error is None
    sandbox_id = response.sandbox_id
    print(f"[OK] Created sandbox for bash tests: {sandbox_id}")

    try:
        # Test echo command
        from sandbox import ExecRequest
        exec_req = ExecRequest(cmd="echo 'Hello from bash'")
        result, error = service.execute(sandbox_id, exec_req)
        assert error is None
        assert result.exit_code == 0
        assert "Hello from bash" in result.stdout
        print(f"[OK] Echo command: {result.stdout.strip()}")

        # Test environment variables
        exec_req = ExecRequest(cmd="echo $HOME")
        result, error = service.execute(sandbox_id, exec_req)
        assert error is None
        assert result.exit_code == 0
        print(f"[OK] HOME env: {result.stdout.strip()}")

        # Test file creation
        exec_req = ExecRequest(cmd="echo 'test content' > /workspace/test.txt")
        result, error = service.execute(sandbox_id, exec_req)
        assert error is None
        assert result.exit_code == 0
        print("[OK] Created test.txt via bash")

        # Test file reading
        exec_req = ExecRequest(cmd="cat /workspace/test.txt")
        result, error = service.execute(sandbox_id, exec_req)
        assert error is None
        assert result.exit_code == 0
        assert "test content" in result.stdout
        print(f"[OK] Read test.txt: {result.stdout.strip()}")

        # Test directory listing
        exec_req = ExecRequest(cmd="ls -la /workspace")
        result, error = service.execute(sandbox_id, exec_req)
        assert error is None
        assert result.exit_code == 0
        print("[OK] Listed workspace directory")

        # Test pipes
        exec_req = ExecRequest(cmd="echo -e 'line1\\nline2\\nline3' | grep line2")
        result, error = service.execute(sandbox_id, exec_req)
        assert error is None
        assert result.exit_code == 0
        assert "line2" in result.stdout
        print("[OK] Pipe command works")

    finally:
        # Cleanup
        service.destroy_sandbox(sandbox_id)
        print("[OK] Cleanup completed")


def test_python_execution():
    """Test executing Python scripts."""
    from sandbox import SandboxService, SandboxCreateRequest, ExecRequest

    service = SandboxService()

    # Create a Python sandbox
    request = SandboxCreateRequest(template="python-basic")
    response, error = service.create_sandbox(request)
    assert error is None
    sandbox_id = response.sandbox_id
    print(f"[OK] Created Python sandbox: {sandbox_id}")

    try:
        # Test python -c
        exec_req = ExecRequest(cmd="python3 -c \"print('Hello from Python')\"")
        result, error = service.execute(sandbox_id, exec_req)
        assert error is None
        assert result.exit_code == 0
        assert "Hello from Python" in result.stdout
        print(f"[OK] python -c: {result.stdout.strip()}")

        # Test Python arithmetic
        exec_req = ExecRequest(cmd="python3 -c \"print(2 + 2)\"")
        result, error = service.execute(sandbox_id, exec_req)
        assert error is None
        assert result.exit_code == 0
        assert "4" in result.stdout
        print(f"[OK] Python arithmetic: {result.stdout.strip()}")

        # Test Python with imports
        exec_req = ExecRequest(cmd="python3 -c \"import json; print(json.dumps({'key': 'value'}))\"")
        result, error = service.execute(sandbox_id, exec_req)
        assert error is None
        assert result.exit_code == 0
        assert "value" in result.stdout
        print(f"[OK] Python json: {result.stdout.strip()}")

        # Test writing a Python file and executing it
        exec_req = ExecRequest(cmd="cat > /workspace/hello.py << 'EOF'\n#!/usr/bin/env python3\nprint('Hello from script file!')\nEOF")
        result, error = service.execute(sandbox_id, exec_req)
        assert error is None

        exec_req = ExecRequest(cmd="python3 /workspace/hello.py")
        result, error = service.execute(sandbox_id, exec_req)
        assert error is None
        assert result.exit_code == 0
        assert "Hello from script file!" in result.stdout
        print(f"[OK] Executed script file: {result.stdout.strip()}")

    finally:
        # Cleanup
        service.destroy_sandbox(sandbox_id)
        print("[OK] Cleanup completed")


def test_multilingual():
    """Test different language templates."""
    from sandbox import SandboxService, SandboxCreateRequest, ExecRequest

    service = SandboxService()

    templates_to_test = [
        ("python-basic", "python3 --version", "Python"),
        ("linux-basic", "echo $SHELL", "Bash"),
    ]

    for template_id, test_cmd, lang_name in templates_to_test:
        request = SandboxCreateRequest(template=template_id)
        response, error = service.create_sandbox(request)
        assert error is None, f"Failed to create {template_id}: {error}"
        sandbox_id = response.sandbox_id

        try:
            exec_req = ExecRequest(cmd=test_cmd)
            result, error = service.execute(sandbox_id, exec_req)
            assert error is None, f"Failed to execute in {template_id}: {error}"
            assert result.exit_code == 0
            print(f"[OK] {template_id}: command executed successfully")
        finally:
            service.destroy_sandbox(sandbox_id)


def test_environment_discovery():
    """Test environment discovery endpoint."""
    from sandbox import SandboxService, SandboxCreateRequest

    service = SandboxService()

    request = SandboxCreateRequest(template="python-basic")
    response, error = service.create_sandbox(request)
    assert error is None
    sandbox_id = response.sandbox_id

    try:
        # Get environment
        env, error = service.get_environment(sandbox_id)
        assert error is None
        assert env.os == "linux"
        assert any("python" in c for c in env.capabilities)
        print(f"[OK] Environment: os={env.os}, arch={env.arch}")
        print(f"[OK] Capabilities: {env.capabilities}")
    finally:
        service.destroy_sandbox(sandbox_id)


def test_filesystem_api():
    """Test filesystem operations."""
    from sandbox import SandboxService, SandboxCreateRequest

    service = SandboxService()

    request = SandboxCreateRequest(template="linux-basic")
    response, error = service.create_sandbox(request)
    assert error is None
    sandbox_id = response.sandbox_id

    try:
        # Write file via API
        success, error = service.write_file(sandbox_id, "api_test.txt", "Content via API")
        assert success
        print("[OK] Wrote file via API")

        # List files
        files, error = service.list_files(sandbox_id, ".")
        assert error is None
        assert any(f.name == "api_test.txt" for f in files.items)
        print(f"[OK] Listed {len(files.items)} items in workspace")

        # Read file via API
        file_resp, error = service.read_file(sandbox_id, "api_test.txt")
        assert error is None
        assert file_resp.content.strip() == "Content via API"
        print(f"[OK] Read file via API: '{file_resp.content.strip()}'")

    finally:
        service.destroy_sandbox(sandbox_id)


def test_files_changed_tracking():
    """Test that file changes are tracked."""
    from sandbox import SandboxService, SandboxCreateRequest, ExecRequest

    service = SandboxService()

    request = SandboxCreateRequest(template="linux-basic")
    response, error = service.create_sandbox(request)
    assert error is None
    sandbox_id = response.sandbox_id

    try:
        # Create a file
        exec_req = ExecRequest(cmd="echo 'version 1' > /workspace/version.txt")
        result, error = service.execute(sandbox_id, exec_req)
        assert error is None

        # Modify the file
        exec_req = ExecRequest(cmd="echo 'version 2' > /workspace/version.txt")
        result, error = service.execute(sandbox_id, exec_req)
        assert error is None

        # Check files_changed is returned
        assert result.files_changed is not None
        print(f"[OK] Files changed: {result.files_changed}")

    finally:
        service.destroy_sandbox(sandbox_id)


def test_workspace_export():
    """Test workspace export."""
    from sandbox import SandboxService, SandboxCreateRequest, ExecRequest

    service = SandboxService()

    request = SandboxCreateRequest(template="linux-basic")
    response, error = service.create_sandbox(request)
    assert error is None
    sandbox_id = response.sandbox_id

    try:
        # Create some files
        exec_req = ExecRequest(cmd="mkdir -p /workspace/output && echo 'data' > /workspace/output/data.txt")
        result, error = service.execute(sandbox_id, exec_req)
        assert error is None

        # Export workspace
        export_resp, error = service.export_workspace(sandbox_id, ".")
        assert error is None
        assert export_resp.artifact_id is not None
        print(f"[OK] Exported workspace: {export_resp.artifact_id} ({export_resp.size} bytes)")

    finally:
        service.destroy_sandbox(sandbox_id)


if __name__ == "__main__":
    print("=" * 60)
    print("Sandbox Integration Tests")
    print("=" * 60)
    print()

    tests = [
        ("Imports", test_imports),
        ("Templates", test_templates),
        ("Storage", test_storage),
        ("Sandbox Lifecycle", test_sandbox_lifecycle),
        ("Bash Commands", test_bash_commands),
        ("Python Execution", test_python_execution),
        ("Multilingual", test_multilingual),
        ("Environment Discovery", test_environment_discovery),
        ("Filesystem API", test_filesystem_api),
        ("Files Changed Tracking", test_files_changed_tracking),
        ("Workspace Export", test_workspace_export),
    ]

    passed = 0
    failed = 0

    for name, test_func in tests:
        print(f"\n--- Test: {name} ---")
        try:
            test_func()
            passed += 1
        except Exception as e:
            print(f"[FAIL] {name}: {e}")
            import traceback
            traceback.print_exc()
            failed += 1

    print()
    print("=" * 60)
    print(f"Results: {passed} passed, {failed} failed")
    print("=" * 60)

    sys.exit(0 if failed == 0 else 1)
