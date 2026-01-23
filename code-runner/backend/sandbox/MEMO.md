# Sandbox 架构改进备忘录

> 生成日期: 2026-01-15
> 状态: 核心功能完成，待优化

---

## 一、已完成 ✅

### 核心架构
- [x] Template 模型（能力定义）
- [x] Sandbox 模型（有状态实例）
- [x] 单入口 Exec (bash 命令执行)
- [x] 环境探测 GET /env
- [x] SQLite 持久化（启动恢复）
- [x] Python SDK (sandbox/sdks/python/__init__.py)
- [x] TypeScript SDK (sandbox/sdks/typescript/)
- [x] C# SDK (sandbox/sdks/csharp/)

### API 端点
- [x] POST /templates - 创建沙箱
- [x] GET /sandboxes/{id} - 查询状态
- [x] GET /sandboxes/{id}/env - 环境探测
- [x] POST /sandboxes/{id}/exec - 执行命令
- [x] GET/POST /files - 文件读写
- [x] POST /export - 导出产物

---

## 二、待改进 ⚠️

### P0 - 高优先级（稳定性）

#### 1. 统一状态管理 ✅ 已完成
```python
# 改进后：统一通过 Repository 模式
class SandboxRepository:
    _cache: Dict           # 内存缓存
    storage: SandboxStorage  # SQLite

    def save(instance):    # 保存到缓存 + SQLite
    def load(id):          # 先查缓存，再查 SQLite
    def update_status(id, status):  # 更新缓存 + SQLite
    def update_file_hashes(id, hashes):  # 更新缓存 + SQLite
    def destroy(id):       # 删除缓存 + SQLite
```

**修改文件：**
- `storage.py`: 增强 `SandboxRepository`，添加缓存管理和统一状态更新
- `runner.py`: 移除 `_instances`，统一通过 `repo` 操作

#### 2. 容器健康检查 ✅ 已完成
```python
# 惰性检查：每次 load() 时验证容器状态
def load(self, sandbox_id):
    # 检查缓存中的容器
    container = self.docker.get_container_by_id(instance.container_id)
    if not container or container.status != "running":
        # 标记为终止
        self.storage.update_status(sandbox_id, "terminated")
```

**实现方式：**
- 惰性检查：每次访问 sandbox 时自动验证容器是否存在
- 无需后台定时任务，零额外开销

#### 3. 执行超时控制 ✅ 已完成
```python
# 改进：支持超时参数，超时自动 kill 并标记 ERROR
from sandbox.docker_service import ExecutionTimeout

# API 调用
POST /sandboxes/{id}/exec
{
    "cmd": "python script.py",
    "timeout": 30  # 秒
}

# 超时返回
{
    "error": "Execution timeout",
    "detail": "Command exceeded 30s timeout"
}
```

**修改文件：**
- `models.py`: `ExecRequest` 添加 `timeout` 字段
- `docker_service.py`: `exec_command()` 添加 `timeout` 参数，超时后 kill 进程
- `runner.py`: `execute()` 调用时传递 timeout，超时标记 sandbox 为 ERROR

---

### P1 - 中优先级（资源）

#### 4. Docker 资源限制 ✅ 已完成
```python
# 改进：根据 template 配置资源限制
DEFAULT_RESOURCES = {
    "memory": "256m",  # 256 MB
    "cpu": 0.5,        # 50% CPU
    "pids": 100        # 100 进程
}

HEAVY_RESOURCES = {
    "memory": "1g",    # 1 GB
    "cpu": 1.0,        # 100% CPU
    "pids": 200        # 200 进程
}

# 创建容器时应用限制
container = self.client.containers.run(
    image=image_name,
    mem_limit="256m",
    cpu_period=100000,
    cpu_quota=50000,
    pids_limit=100,
)
```

**修改文件：**
- `config.py`: 添加 `DEFAULT_RESOURCES` 和 `HEAVY_RESOURCES` 配置
- `docker_service.py`: `create_container()` 添加 `resources` 参数
- `runner.py`: `create_sandbox()` 调用时传递 template 的资源限制
- `sdks/python/__init__.py`: Python SDK 已实现超时参数

#### 5. 并发控制
```python
# 当前：无并发控制
# 建议：添加资源池
- 最大并发沙箱数
- 资源配额（CPU/内存总量）
- 队列等待机制
```

---

### P2 - 低优先级（运维）

#### 6. 执行日志记录
```python
# 当前：exec 输出只返回给调用方
# 建议：持久化执行日志
ExecutionLog:
  - execution_id
  - sandbox_id
  - command
  - exit_code
  - stdout (截断)
  - stderr (截断)
  - duration_ms
  - created_at
```

#### 7. 网络隔离
```python
# 当前：容器可访问外网
# 建议：可选网络策略
network_mode="none"  # 禁止网络
# 或
network_mode="internal"  # 仅内部网络
```

#### 8. 审计日志
```python
# 当前：只有代码中的 logger.info
# 建议：结构化审计日志
AuditLog:
  - action (create/exec/destroy)
  - user_id
  - sandbox_id
  - timestamp
  - details
```

---

## 七、多语言 SDK

### 目录结构
```
sandbox/sdks/
├── python/           # Python SDK
│   ├── __init__.py   # SandboxClient, models
│   └── test_client.py
├── typescript/       # TypeScript SDK
│   ├── src/
│   │   ├── index.ts
│   │   ├── client.ts
│   │   └── models.ts
│   ├── package.json
│   ├── tsconfig.json
│   └── package-lock.json
└── csharp/           # C# SDK
    ├── src/          # SDK 发布包
    │   ├── SandboxClient.cs
    │   ├── Models.cs
    │   └── SandboxSdk.csproj
    └── tests/        # 单元测试
        ├── SandboxClientTests.cs
        └── SandboxSdk.Tests.csproj
```

### 统一 API 设计
```python
# 各语言保持一致的 API
client = SandboxClient(base_url="http://localhost:8002")

# 生命周期
sandbox = await client.create_sandbox("python-basic")
await client.destroy(sandbox.id)

# 执行
result = await client.exec(sandbox.id, "python script.py", timeout=60)

# 文件
await client.write_file(sandbox.id, "test.py", content)
files = await client.list_files(sandbox.id)
```

### 使用示例

**Python:**
```python
from sandbox_sdks.python import SandboxClient

async with SandboxClient() as client:
    sandbox = await client.create_sandbox("python-basic")
    result = await client.exec(sandbox.id, "python --version")
```

**TypeScript:**
```typescript
import { SandboxClient } from "@code-runner/sandbox-sdk";

const client = new SandboxClient({ baseUrl: "http://localhost:8002" });
const sandbox = await client.createSandbox("python-basic");
const result = await client.exec(sandbox.id, "python --version");
```

**C#:**
```csharp
using CodeRunner.SandboxSdk;

using var client = new SandboxClient("http://localhost:8002");
var sandbox = await client.CreateSandboxAsync("python-basic");
var result = await client.ExecAsync(sandbox.Id, "python --version");
```

---

## 三、架构调整建议

### 统一存储层
```
# 建议的存储结构
sandbox/
├── storage/
│   ├── __init__.py
│   ├── repository.py      # SandboxRepository
│   ├── models.py          # SandboxRecord
│   └── migrations/        # 数据库迁移
├── services/
│   ├── docker.py          # Docker 操作
│   ├── health.py          # 健康检查
│   └── resource.py        # 资源管理
└── scheduler/             # 定时任务
    ├── cleanupExpired.py
    └── healthCheck.py
```

### 配置文件
```yaml
# sandbox/config.yaml
sandbox:
  max_instances: 20
  default_timeout: 300  # 秒
  cleanup_interval: 60  # 秒
  auto_recovery: true

resources:
  memory: "1g"
  cpu: "1.0"
  pids_limit: 100

docker:
  network: "none"  # 或 "bridge"
  volume_driver: "local"
```

---

## 四、测试覆盖

### 当前测试
- ✅ Imports
- ✅ Templates
- ✅ Storage
- ✅ Lifecycle
- ✅ Bash Commands
- ✅ Python Execution
- ✅ Multilingual
- ✅ Environment Discovery
- ✅ Filesystem API
- ✅ Files Changed Tracking
- ✅ Workspace Export
- ✅ 资源限制 (256MB/1GB, 50%/100% CPU, 100/200 PIDs)
- ✅ 超时控制 (timeout 参数)

### 待补充测试
- [x] 健康检查 (惰性检查已实现)
- [x] 资源限制 (已实现)
- [x] 超时控制 (已实现)
- [ ] 并发场景
- [ ] 网络隔离
- [ ] 故障恢复（容器崩溃）

---

## 五、结对编程任务清单

### Session 1: 基础稳定性 ✅ 已完成
- [x] 重构 Repository 模式，统一存储
- [x] 添加健康检查（惰性检查，无需定时任务）
- [x] 添加执行超时控制

### Session 2: 资源管理 ✅ 已完成
- [x] 添加 Docker 资源限制
- [x] 配置默认/高资源模板
- [ ] 实现并发控制（待完成）
- [ ] 添加配置文件 (可选)

### Session 3: SDK 开发 ✅ 已完成
- [x] Python SDK (13 tests)
- [x] TypeScript SDK (15 tests)
- [x] C# SDK (14 tests)

### Session 4: Legacy 验证 ✅ 已完成
- [x] 移动 legacy 代码到 legacy/ 目录
- [x] 清理 sandbox_client.py (旧版)
- [x] 验证 legacy 集成测试 (22 passed)

---

## 八、后续工作

- 并发控制
- 执行日志持久化
- 网络隔离选项
- 审计日志

---

## 参考项目

- Docker SDK: https://docker-py.readthedocs.io/
- FastAPI: https://fastapi.tiangolo.com/
- SQLite: https://docs.python.org/3/library/sqlite3.html

---

> "先让它跑起来，再让它跑得快" - 当前核心功能已完成，稳定性和资源管理后续优化。

**下班！🛫**
