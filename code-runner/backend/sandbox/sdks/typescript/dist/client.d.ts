/**
 * Sandbox SDK Client for TypeScript
 */
import type { Sandbox, SandboxDetail, Environment, ExecResult, FileItem, FileContent, ExportResult, Template, ExecOptions, CreateSandboxOptions, DestroyOptions } from "./models";
export declare class SandboxClient {
    private baseUrl;
    private fetch;
    constructor(options?: {
        baseUrl?: string;
        fetch?: typeof fetch;
    });
    private request;
    listTemplates(): Promise<Template[]>;
    getTemplate(templateId: string): Promise<Template>;
    createSandbox(template: string, options?: CreateSandboxOptions): Promise<Sandbox>;
    getSandbox(sandboxId: string): Promise<SandboxDetail>;
    listSandboxes(): Promise<Sandbox[]>;
    destroy(sandboxId: string, options?: DestroyOptions): Promise<void>;
    getEnvironment(sandboxId: string): Promise<Environment>;
    exec(sandboxId: string, cmd: string, options?: ExecOptions): Promise<ExecResult>;
    execAndCheck(sandboxId: string, cmd: string, options?: ExecOptions): Promise<ExecResult>;
    writeFile(sandboxId: string, path: string, content: string): Promise<void>;
    readFile(sandboxId: string, path: string): Promise<FileContent>;
    listFiles(sandboxId: string, path?: string): Promise<FileItem[]>;
    export(sandboxId: string, path?: string): Promise<ExportResult>;
    uploadWorkspace(sandboxId: string, sourcePath: string, clearFirst?: boolean): Promise<void>;
    syncFiles(sandboxId: string, files: Record<string, string>): Promise<number>;
    runScript(sandboxId: string, script: string, timeout?: number): Promise<ExecResult>;
}
