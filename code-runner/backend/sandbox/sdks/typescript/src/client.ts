/**
 * Sandbox SDK Client for TypeScript
 */

import type {
  Sandbox,
  SandboxDetail,
  Environment,
  ExecResult,
  FileItem,
  FileContent,
  ExportResult,
  Template,
  ExecOptions,
  CreateSandboxOptions,
  DestroyOptions,
} from "./models";

// API response types (camelCase to snake_case conversion)
interface ApiSandboxResponse {
  sandbox_id: string;
  status: string;
  paths: { workspace: string };
  runtime: { image: string; resolved_from: string };
  created_at: string;
}

interface ApiSandboxDetailResponse {
  sandbox_id: string;
  template: string;
  status: string;
  paths: { workspace: string };
  runtime: { image: string; resolved_from: string };
  created_at: string;
  expires_at?: string;
}

interface ApiFileListResponse {
  items: Array<{ name: string; path: string; is_dir: boolean; size?: number }>;
}

interface ApiExportResponse {
  artifact_id: string;
  path: string;
  size: number;
  download_url: string;
}

interface ApiExecResponse {
  execution_id: string;
  exit_code: number;
  stdout: string;
  stderr: string;
  duration_ms: number;
  files_changed: string[];
}

export class SandboxClient {
  private baseUrl: string;
  private fetch: typeof fetch;

  constructor(options: {
    baseUrl?: string;
    fetch?: typeof fetch;
  } = {}) {
    this.baseUrl = (options.baseUrl || "http://localhost:8002").replace(/\/$/, "");
    this.fetch = options.fetch || fetch;
  }

  // Transform API response to Sandbox model
  private transformSandboxResponse(response: ApiSandboxResponse): Sandbox {
    return {
      id: response.sandbox_id,
      status: response.status,
      workdir: response.paths.workspace,
      template: response.runtime.resolved_from.replace("template:", ""),
      createdAt: response.created_at
    };
  }

  private async request<T>(
    method: string,
    path: string,
    options: {
      body?: unknown;
      params?: Record<string, string>;
    } = {}
  ): Promise<T> {
    const url = new URL(path, this.baseUrl);
    if (options.params) {
      Object.entries(options.params).forEach(([key, value]) => {
        url.searchParams.append(key, value);
      });
    }

    const response = await this.fetch(url.toString(), {
      method,
      headers: {
        "Content-Type": "application/json",
      },
      body: options.body ? JSON.stringify(options.body) : undefined,
    });

    if (!response.ok) {
      const error = await response.text();
      throw new Error(`HTTP ${response.status}: ${error}`);
    }

    return response.json() as Promise<T>;
  }

  // ============ Templates ============

  async listTemplates(): Promise<Template[]> {
    const response = await this.request<{ templates: Template[] }>(
      "GET",
      "/api/sandbox/templates"
    );
    return response.templates;
  }

  async getTemplate(templateId: string): Promise<Template> {
    return this.request<Template>("GET", `/api/sandbox/templates/${templateId}`);
  }

  // ============ Sandbox Lifecycle ============

  async createSandbox(
    template: string,
    options: CreateSandboxOptions = {}
  ): Promise<Sandbox> {
    const body: Record<string, unknown> = { template };
    if (options.workspaceFiles) {
      body.workspace = { files: options.workspaceFiles };
    }
    const response = await this.request<ApiSandboxResponse>("POST", "/api/sandbox/sandboxes", { body });
    return this.transformSandboxResponse(response);
  }

  async getSandbox(sandboxId: string): Promise<SandboxDetail> {
    const response = await this.request<ApiSandboxDetailResponse>(
      "GET",
      `/api/sandbox/sandboxes/${sandboxId}`
    );
    return {
      id: response.sandbox_id,
      template: response.template,
      status: response.status,
      workdir: response.paths.workspace,
      createdAt: response.created_at,
      expiresAt: response.expires_at
    };
  }

  async listSandboxes(): Promise<Sandbox[]> {
    const response = await this.request<ApiSandboxResponse[]>("GET", "/api/sandbox/sandboxes");
    return response.map(r => this.transformSandboxResponse(r));
  }

  async destroy(sandboxId: string, options: DestroyOptions = {}): Promise<void> {
    const params: Record<string, string> = {};
    if (options.exportPath) {
      params.export = options.exportPath;
    }
    await this.request(
      "DELETE",
      `/api/sandbox/sandboxes/${sandboxId}`,
      { params }
    );
  }

  // ============ Environment ============

  async getEnvironment(sandboxId: string): Promise<Environment> {
    return this.request<Environment>(
      "GET",
      `/api/sandbox/sandboxes/${sandboxId}/env`
    );
  }

  // ============ Execution ============

  async exec(
    sandboxId: string,
    cmd: string,
    options: ExecOptions = {}
  ): Promise<ExecResult> {
    const body: Record<string, unknown> = { cmd };
    if (options.cwd) body.cwd = options.cwd;
    if (options.env) body.env = options.env;
    if (options.timeout) body.timeout = options.timeout;

    const response = await this.request<ApiExecResponse>(
      "POST",
      `/api/sandbox/sandboxes/${sandboxId}/exec`,
      { body }
    );
    return {
      executionId: response.execution_id,
      exitCode: response.exit_code,
      stdout: response.stdout,
      stderr: response.stderr,
      durationMs: response.duration_ms,
      filesChanged: response.files_changed
    };
  }

  async execAndCheck(
    sandboxId: string,
    cmd: string,
    options: ExecOptions = {}
  ): Promise<ExecResult> {
    const result = await this.exec(sandboxId, cmd, options);
    if (result.exitCode !== 0) {
      throw new Error(
        `Command failed with exit code ${result.exitCode}:\n${result.stderr}`
      );
    }
    return result;
  }

  // ============ File Operations ============

  async writeFile(
    sandboxId: string,
    path: string,
    content: string
  ): Promise<void> {
    await this.request(
      "POST",
      `/api/sandbox/sandboxes/${sandboxId}/write`,
      { body: { path, content } }
    );
  }

  async readFile(sandboxId: string, path: string): Promise<FileContent> {
    return this.request<FileContent>(
      "GET",
      `/api/sandbox/sandboxes/${sandboxId}/file`,
      { params: { path } }
    );
  }

  async listFiles(sandboxId: string, path: string = "."): Promise<FileItem[]> {
    const response = await this.request<ApiFileListResponse>(
      "GET",
      `/api/sandbox/sandboxes/${sandboxId}/files`,
      { params: { path } }
    );
    return response.items.map(item => ({
      name: item.name,
      path: item.path,
      isDir: item.is_dir,
      size: item.size
    }));
  }

  // ============ Workspace Operations ============

  async export(sandboxId: string, path: string = "."): Promise<ExportResult> {
    const response = await this.request<ApiExportResponse>(
      "POST",
      `/api/sandbox/sandboxes/${sandboxId}/export`,
      { body: { path, as_artifact: true } }
    );
    return {
      artifactId: response.artifact_id,
      path: response.path,
      size: response.size,
      downloadUrl: response.download_url
    };
  }

  async uploadWorkspace(
    sandboxId: string,
    sourcePath: string,
    clearFirst: boolean = false
  ): Promise<void> {
    await this.request(
      "POST",
      `/api/sandbox/sandboxes/${sandboxId}/upload`,
      {
        params: { clear_first: String(clearFirst).toLowerCase() },
        body: { source_path: sourcePath },
      }
    );
  }

  async syncFiles(
    sandboxId: string,
    files: Record<string, string>
  ): Promise<number> {
    const response = await this.request<{ synced: number }>(
      "POST",
      `/api/sandbox/sandboxes/${sandboxId}/sync`,
      { body: files }
    );
    return response.synced;
  }

  // ============ Convenience Methods ============

  async runScript(
    sandboxId: string,
    script: string,
    timeout?: number
  ): Promise<ExecResult> {
    const scriptPath = "/tmp/script.sh";
    await this.writeFile(sandboxId, scriptPath, `#!/bin/bash\nset -e\n${script}`);
    return this.execAndCheck(sandboxId, `bash ${scriptPath}`, { timeout });
  }
}

// ============ Example Usage ============

/*
import { SandboxClient } from "./client";

async function example() {
  const client = new SandboxClient({ baseUrl: "http://localhost:8002" });

  // List templates
  const templates = await client.listTemplates();
  console.log("Templates:", templates.map(t => t.id));

  // Create sandbox
  const sandbox = await client.createSandbox("python-basic");
  console.log("Created:", sandbox.id);

  // Execute command
  const result = await client.exec(sandbox.id, "python --version");
  console.log("Python:", result.stdout.trim());

  // Write and run script
  await client.writeFile(sandbox.id, "hello.py", 'print("Hello!")');
  await client.execAndCheck(sandbox.id, "python hello.py");

  // Destroy
  await client.destroy(sandbox.id);
  console.log("Done!");
}

example().catch(console.error);
*/
