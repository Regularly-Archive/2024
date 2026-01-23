/**
 * Unit tests for the TypeScript Sandbox SDK.
 */
import { SandboxClient } from './client';
import type {
  Sandbox,
  SandboxDetail,
  Environment,
  ExecResult,
  FileItem,
  FileContent,
  ExportResult,
  Template,
} from './models';
import { execResultSuccess } from './models';

// Mock fetch
const mockFetch = jest.fn();

interface MockResponse {
  ok: boolean;
  status: number;
  json: jest.Mock;
  text: jest.Mock;
}

const mockResponse: MockResponse = {
  ok: true,
  status: 200,
  json: jest.fn(),
  text: jest.fn(),
};

beforeEach(() => {
  mockFetch.mockReset();
  mockResponse.ok = true;
  mockResponse.status = 200;
  mockResponse.json.mockReset();
  mockResponse.text.mockReset();
  mockFetch.mockResolvedValue(mockResponse);
});

describe('Models', () => {
  describe('execResultSuccess', () => {
    it('returns true for exit code 0', () => {
      const result: ExecResult = {
        executionId: 'exec_123',
        exitCode: 0,
        stdout: 'Hello',
        stderr: '',
        durationMs: 100,
        filesChanged: [],
      };
      expect(execResultSuccess(result)).toBe(true);
    });

    it('returns false for non-zero exit code', () => {
      const result: ExecResult = {
        executionId: 'exec_123',
        exitCode: 1,
        stdout: '',
        stderr: 'Error',
        durationMs: 100,
        filesChanged: [],
      };
      expect(execResultSuccess(result)).toBe(false);
    });
  });
});

describe('SandboxClient', () => {
  let client: SandboxClient;

  beforeEach(() => {
    client = new SandboxClient({ baseUrl: 'http://localhost:8002', fetch: mockFetch as unknown as typeof fetch });
  });

  describe('listTemplates', () => {
    it('returns list of templates', async () => {
      mockResponse.json.mockResolvedValue({
        templates: [
          {
            id: 'python-basic',
            description: 'Python runtime',
            capabilities: ['bash', 'python@3.11'],
            defaults: { workdir: '/workspace' },
            constraints: {},
          },
        ],
      });

      const templates = await client.listTemplates();

      expect(templates).toHaveLength(1);
      expect(templates[0].id).toBe('python-basic');
      expect(mockFetch).toHaveBeenCalledWith(
        'http://localhost:8002/api/sandbox/templates',
        expect.objectContaining({ method: 'GET' })
      );
    });
  });

  describe('createSandbox', () => {
    it('creates a sandbox', async () => {
      mockResponse.json.mockResolvedValue({
        sandbox_id: 'sbx_test123',
        status: 'running',
        paths: { workspace: '/workspace' },
        runtime: { image: 'python:3.11', resolved_from: 'template:python-basic' },
        created_at: '2024-01-01T00:00:00',
      });

      const sandbox = await client.createSandbox('python-basic');

      expect(sandbox.id).toBe('sbx_test123');
      expect(sandbox.template).toBe('python-basic');
      expect(mockFetch).toHaveBeenCalledWith(
        'http://localhost:8002/api/sandbox/sandboxes',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ template: 'python-basic' }),
        })
      );
    });

    it('creates sandbox with workspace files', async () => {
      mockResponse.json.mockResolvedValue({
        sandbox_id: 'sbx_test123',
        status: 'running',
        paths: { workspace: '/workspace' },
        runtime: { image: 'python:3.11', resolved_from: 'template:python-basic' },
        created_at: '2024-01-01T00:00:00',
      });

      await client.createSandbox('python-basic', { workspaceFiles: 'artifact://files.zip' });

      expect(mockFetch).toHaveBeenCalledWith(
        'http://localhost:8002/api/sandbox/sandboxes',
        expect.objectContaining({
          body: JSON.stringify({
            template: 'python-basic',
            workspace: { files: 'artifact://files.zip' },
          }),
        })
      );
    });
  });

  describe('exec', () => {
    it('executes a command', async () => {
      mockResponse.json.mockResolvedValue({
        execution_id: 'exec_123',
        exit_code: 0,
        stdout: 'Hello',
        stderr: '',
        duration_ms: 100,
        files_changed: [],
      });

      const result = await client.exec('sbx_test', 'echo hello');

      expect(result.exitCode).toBe(0);
      expect(result.stdout).toBe('Hello');
      expect(mockFetch).toHaveBeenCalledWith(
        'http://localhost:8002/api/sandbox/sandboxes/sbx_test/exec',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ cmd: 'echo hello' }),
        })
      );
    });

    it('executes with timeout', async () => {
      mockResponse.json.mockResolvedValue({
        execution_id: 'exec_123',
        exit_code: 0,
        stdout: 'Done',
        stderr: '',
        duration_ms: 1000,
        files_changed: [],
      });

      await client.exec('sbx_test', 'sleep 30', { timeout: 60 });

      expect(mockFetch).toHaveBeenCalledWith(
        'http://localhost:8002/api/sandbox/sandboxes/sbx_test/exec',
        expect.objectContaining({
          body: JSON.stringify({ cmd: 'sleep 30', timeout: 60 }),
        })
      );
    });
  });

  describe('getEnvironment', () => {
    it('returns environment info', async () => {
      mockResponse.json.mockResolvedValue({
        os: 'linux',
        arch: 'amd64',
        capabilities: ['bash', 'python@3.11'],
        paths: { workspace: '/workspace' },
      });

      const env = await client.getEnvironment('sbx_test');

      expect(env.os).toBe('linux');
      expect(env.arch).toBe('amd64');
      expect(env.capabilities).toContain('python@3.11');
    });
  });

  describe('writeFile', () => {
    it('writes a file', async () => {
      mockResponse.json.mockResolvedValue({ status: 'ok' });

      await client.writeFile('sbx_test', 'test.py', 'print("hello")');

      expect(mockFetch).toHaveBeenCalledWith(
        'http://localhost:8002/api/sandbox/sandboxes/sbx_test/write',
        expect.objectContaining({
          method: 'POST',
          body: JSON.stringify({ path: 'test.py', content: 'print("hello")' }),
        })
      );
    });
  });

  describe('listFiles', () => {
    it('lists files in directory', async () => {
      mockResponse.json.mockResolvedValue({
        items: [
          { name: 'file.txt', path: '/workspace/file.txt', is_dir: false, size: 1024 },
          { name: 'folder', path: '/workspace/folder', is_dir: true },
        ],
      });

      const files = await client.listFiles('sbx_test', '/workspace');

      expect(files).toHaveLength(2);
      expect(files[0].name).toBe('file.txt');
      expect(files[0].isDir).toBe(false);
      expect(files[1].isDir).toBe(true);
    });
  });

  describe('export', () => {
    it('exports workspace', async () => {
      mockResponse.json.mockResolvedValue({
        artifact_id: 'art_123',
        path: '.',
        size: 1024,
        download_url: '/api/sandbox/artifacts/sbx_test/art_123.zip',
      });

      const result = await client.export('sbx_test', '.');

      expect(result.artifactId).toBe('art_123');
      expect(result.downloadUrl).toContain('art_123');
    });
  });

  describe('destroy', () => {
    it('destroys a sandbox', async () => {
      mockResponse.json.mockResolvedValue({ status: 'ok' });

      await client.destroy('sbx_test');

      expect(mockFetch).toHaveBeenCalledWith(
        'http://localhost:8002/api/sandbox/sandboxes/sbx_test',
        expect.objectContaining({ method: 'DELETE' })
      );
    });

    it('exports before destroying', async () => {
      mockResponse.json.mockResolvedValue({ status: 'ok' });

      await client.destroy('sbx_test', { exportPath: 'output' });

      expect(mockFetch).toHaveBeenCalledWith(
        'http://localhost:8002/api/sandbox/sandboxes/sbx_test?export=output',
        expect.objectContaining({ method: 'DELETE' })
      );
    });
  });

  describe('error handling', () => {
    it('throws on HTTP error', async () => {
      mockResponse.ok = false;
      mockResponse.status = 404;
      mockResponse.text.mockResolvedValue('Not Found');

      await expect(client.getSandbox('sbx_nonexistent')).rejects.toThrow('HTTP 404');
    });
  });
});

describe('execAndCheck', () => {
  it('throws on command failure', async () => {
    mockResponse.json.mockResolvedValue({
      execution_id: 'exec_123',
      exit_code: 1,
      stdout: '',
      stderr: 'Command failed',
      duration_ms: 100,
      files_changed: [],
    });

    const client = new SandboxClient({ baseUrl: 'http://localhost:8002', fetch: mockFetch as unknown as typeof fetch });

    await expect(client.execAndCheck('sbx_test', 'failing_command')).rejects.toThrow('Command failed');
  });
});
