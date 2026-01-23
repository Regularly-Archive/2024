/**
 * Sandbox SDK Models for TypeScript
 */
export interface Sandbox {
    id: string;
    status: string;
    workdir: string;
    template: string;
    createdAt: string;
}
export interface SandboxDetail {
    id: string;
    status: string;
    template: string;
    workdir: string;
    createdAt: string;
    expiresAt?: string;
}
export interface Environment {
    os: string;
    arch: string;
    capabilities: string[];
    paths: Record<string, string>;
}
export interface ExecResult {
    executionId: string;
    exitCode: number;
    stdout: string;
    stderr: string;
    durationMs: number;
    filesChanged: string[];
}
export declare function execResultSuccess(result: ExecResult): boolean;
export interface FileItem {
    name: string;
    path: string;
    isDir: boolean;
    size?: number;
}
export interface FileContent {
    path: string;
    content: string;
    size: number;
}
export interface ExportResult {
    artifactId: string;
    path: string;
    size: number;
    downloadUrl: string;
}
export interface Template {
    id: string;
    description: string;
    capabilities: string[];
    defaults: Record<string, string>;
    constraints: Record<string, unknown>;
}
export interface ExecOptions {
    cwd?: string;
    env?: Record<string, string>;
    timeout?: number;
}
export interface CreateSandboxOptions {
    workspaceFiles?: string;
}
export interface DestroyOptions {
    exportPath?: string;
}
