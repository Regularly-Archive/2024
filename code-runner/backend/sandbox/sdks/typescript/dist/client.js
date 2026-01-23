/**
 * Sandbox SDK Client for TypeScript
 */
export class SandboxClient {
    constructor(options = {}) {
        this.baseUrl = (options.baseUrl || "http://localhost:8002").replace(/\/$/, "");
        this.fetch = options.fetch || fetch;
    }
    async request(method, path, options = {}) {
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
        return response.json();
    }
    // ============ Templates ============
    async listTemplates() {
        const response = await this.request("GET", "/api/sandbox/templates");
        return response.templates;
    }
    async getTemplate(templateId) {
        return this.request("GET", `/api/sandbox/templates/${templateId}`);
    }
    // ============ Sandbox Lifecycle ============
    async createSandbox(template, options = {}) {
        const body = { template };
        if (options.workspaceFiles) {
            body.workspace = { files: options.workspaceFiles };
        }
        return this.request("POST", "/api/sandbox/sandboxes", { body });
    }
    async getSandbox(sandboxId) {
        return this.request("GET", `/api/sandbox/sandboxes/${sandboxId}`);
    }
    async listSandboxes() {
        return this.request("GET", "/api/sandbox/sandboxes");
    }
    async destroy(sandboxId, options = {}) {
        const params = {};
        if (options.exportPath) {
            params.export = options.exportPath;
        }
        await this.request("DELETE", `/api/sandbox/sandboxes/${sandboxId}`, { params });
    }
    // ============ Environment ============
    async getEnvironment(sandboxId) {
        return this.request("GET", `/api/sandbox/sandboxes/${sandboxId}/env`);
    }
    // ============ Execution ============
    async exec(sandboxId, cmd, options = {}) {
        const body = { cmd };
        if (options.cwd)
            body.cwd = options.cwd;
        if (options.env)
            body.env = options.env;
        if (options.timeout)
            body.timeout = options.timeout;
        return this.request("POST", `/api/sandbox/sandboxes/${sandboxId}/exec`, { body });
    }
    async execAndCheck(sandboxId, cmd, options = {}) {
        const result = await this.exec(sandboxId, cmd, options);
        if (result.exitCode !== 0) {
            throw new Error(`Command failed with exit code ${result.exitCode}:\n${result.stderr}`);
        }
        return result;
    }
    // ============ File Operations ============
    async writeFile(sandboxId, path, content) {
        await this.request("POST", `/api/sandbox/sandboxes/${sandboxId}/write`, { body: { path, content } });
    }
    async readFile(sandboxId, path) {
        return this.request("GET", `/api/sandbox/sandboxes/${sandboxId}/file`, { params: { path } });
    }
    async listFiles(sandboxId, path = ".") {
        const response = await this.request("GET", `/api/sandbox/sandboxes/${sandboxId}/files`, { params: { path } });
        return response.items;
    }
    // ============ Workspace Operations ============
    async export(sandboxId, path = ".") {
        return this.request("POST", `/api/sandbox/sandboxes/${sandboxId}/export`, { body: { path, as_artifact: true } });
    }
    async uploadWorkspace(sandboxId, sourcePath, clearFirst = false) {
        await this.request("POST", `/api/sandbox/sandboxes/${sandboxId}/upload`, {
            params: { clear_first: String(clearFirst).toLowerCase() },
            body: { source_path: sourcePath },
        });
    }
    async syncFiles(sandboxId, files) {
        const response = await this.request("POST", `/api/sandbox/sandboxes/${sandboxId}/sync`, { body: files });
        return response.synced;
    }
    // ============ Convenience Methods ============
    async runScript(sandboxId, script, timeout) {
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
