# 数据库自然语言查询基础设施设计 (Text2DB)

## 概述

将自然语言转换为数据库查询（Text2DB），支持多种主流数据库的统一抽象。

## 现有实现分析

### Text2SQLPlugin (MySQL)
- 使用 SqlSugar 访问 MySQL
- 通过 `INFORMATION_SCHEMA` 获取表结构
- 使用 Prompt 模板生成 SQL
- **执行 SQL** 并返回 Markdown 表格

### Text2MongoDBPlugin
- 使用 MongoDB.Driver
- 获取集合名称和样本文档
- 使用 Prompt 生成 MongoDB 脚本
- **仅生成脚本**，不执行

## 核心概念：数据源 (DataSource)

引入数据源作为查询的入口，统一管理各种数据来源：

**设计动机：**
- 敏感信息（连接字符串）存储在数据库中，不暴露在插件配置里
- 用户只需配置一次，后续复用更方便
- 集中管理数据源，便于权限控制

### 产物存储

- **存储位置**: 本地文件系统（与现有 ArtifactsPlugin 共用存储目录）
- **访问方式**: 通过现有的 Artifact 下载/预览接口

### 数据源类型

| 类型 | 说明 | 示例 |
|------|------|------|
| MySQL | 关系型数据库 | 业务数据库 |
| PostgreSQL | 关系型数据库 | 业务数据库 |
| SQLServer | 关系型数据库 | 业务数据库 |
| Oracle | 关系型数据库 | 业务数据库 |
| SQLite | 嵌入式数据库 | 本地文件 |
| DuckDB | 分析型数据库 | 本地文件 |
| MongoDB | 文档数据库 | MongoDB Atlas |
| Excel | 电子表格 | .xlsx, .xls |
| CSV | 文本文件 | .csv |
| JSON | 文本文件 | .json |

### 技术栈

| 数据源类型 | 驱动/库 |
|------------|---------|
| 关系型 (MySQL/PostgreSQL/SQLServer/Oracle/SQLite) | SqlSugar |
| DuckDB | DuckDB.NET |
| MongoDB | MongoDB.Driver |
| Excel/CSV/JSON | 相应解析库（ClosedXML, CsvHelper, System.Text.Json） |

### 数据源实体

> 注：CreatedAt/CreatedBy/UpdatedAt/UpdatedBy 等基础字段已在 BaseEntity 中定义

```csharp
// Domain/Entities/DataSource.cs
[SugarTable("data_sources")]
public class DataSource : BaseEntity
{
    /// <summary>数据源名称</summary>
    public string Name { get; set; }

    /// <summary>数据源类型</summary>
    public DataSourceType Type { get; set; }

    /// <summary>连接字符串 / 文件路径 / 配置 JSON</summary>
    public string ConnectionString { get; set; }

    /// <summary>描述</summary>
    public string Description { get; set; }

    /// <summary>所属应用ID</summary>
    public long AppId { get; set; }

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; }
}

public enum DataSourceType
{
    // 关系型数据库
    MySQL = 1,
    PostgreSQL = 2,
    SQLServer = 3,
    Oracle = 4,
    SQLite = 5,
    DuckDB = 6,

    // NoSQL
    MongoDB = 10,

    // 文件
    Excel = 20,
    CSV = 21,
    JSON = 22
}
```

## 架构设计

### 1. 核心接口

#### IDataSourceConnector - 数据源连接器

```csharp
public interface IDataSourceConnector
{
    /// <summary>是否支持执行查询（MongoDB 只生成不执行）</summary>
    bool CanExecute { get; }

    /// <summary>连接数据源</summary>
    Task ConnectAsync(string connectionString);

    /// <summary>获取数据结构（表/集合 + 列 + 样例数据）</summary>
    Task<DatabaseSchema> GetSchemaAsync();

    /// <summary>执行查询</summary>
    Task<QueryResult> ExecuteQueryAsync(string query);

    /// <summary>释放资源</summary>
    Task DisposeAsync();
}
```

#### IQueryGenerator - 查询生成器

```csharp
public interface IQueryGenerator
{
    /// <summary>生成查询脚本/SQL</summary>
    Task<string> GenerateAsync(DatabaseSchema schema, string userQuestion);

    /// <summary>格式化执行结果</summary>
    Task<string> FormatResultAsync(QueryResult result);
}
```

#### 核心模型

```csharp
public class DatabaseSchema
{
    /// <summary>数据源类型</summary>
    public DataSourceType Type { get; set; }

    /// <summary>表/集合列表</summary>
    public List<TableInfo> Tables { get; set; } = new();
}

public class TableInfo
{
    /// <summary>表名</summary>
    public string Name { get; set; }

    /// <summary>表描述/注释</summary>
    public string Description { get; set; }

    /// <summary>列信息</summary>
    public List<ColumnInfo> Columns { get; set; } = new();

    /// <summary>样例数据（可选，Agent 吃不准字段含义时获取，每表 3 条）</summary>
    public List<Dictionary<string, object>> SampleData { get; set; } = new();
}

public class ColumnInfo
{
    public string Name { get; set; }
    public string DataType { get; set; }
    public string Description { get; set; }
    public bool IsNullable { get; set; }
}

public class QueryResult
{
    /// <summary>查询类型</summary>
    public QueryType QueryType { get; set; }

    /// <summary>原始数据</summary>
    public object Data { get; set; }

    /// <summary>执行时间</summary>
    public long ExecutionTimeMs { get; set; }

    /// <summary>返回/影响行数</summary>
    public int RowCount { get; set; }
}
```

### 2. 接口职责划分

| 接口 | 职责 | 示例 |
|------|------|------|
| IDataSourceConnector | 连接、执行、Schema 获取 | MySqlConnector 执行 SQL |
| IQueryGenerator | Prompt 生成、结果格式化 | SqlQueryGenerator 生成 SQL |

**设计原则**：
- Connector 专注"怎么连接、怎么查"
- Generator 专注"怎么根据 Schema 生成查询、怎么格式化结果"

### 2. 目录结构

```
Infrastructure/
└── Text2DB/
    ├── Abstractions/
    │   ├── IDataSourceConnector.cs   # 数据源连接器
    │   ├── IQueryGenerator.cs       # 查询生成器
    │   └── IText2DBService.cs        # 统一服务接口
    │
    ├── Connectors/
    │   ├── Relational/
    │   │   ├── MySqlConnector.cs
    │   │   ├── PostgreSqlConnector.cs
    │   │   ├── SqlServerConnector.cs
    │   │   ├── OracleConnector.cs
    │   │   ├── SQLiteConnector.cs
    │   │   └── DuckDbConnector.cs
    │   │
    │   ├── NoSQL/
    │   │   └── MongoDbConnector.cs
    │   │
    │   └── File/
    │       ├── ExcelConnector.cs
    │       ├── CsvConnector.cs
    │       └── JsonConnector.cs
    │
    ├── Generators/
    │   ├── SqlQueryGenerator.cs      # SQL 系生成器
    │   ├── MongoDbGenerator.cs      # MongoDB 生成器
    │   └── FileQueryGenerator.cs     # 文件查询生成器
    │
    ├── Text2DBService.cs           # 统一入口
    └── DataSourceRegistry.cs         # 连接器注册表
```

### 3. 统一服务

```csharp
public class Text2DBService
{
    private readonly DataSourceRegistry _registry;

    public async Task<QueryResult> QueryAsync(
        long dataSourceId,
        string userQuestion)
    {
        // 1. 获取数据源配置
        var dataSource = await _dataSourceRepository.GetAsync(dataSourceId);

        // 2. 获取对应连接器
        var connector = _registry.GetConnector(dataSource.Type);

        // 3. 连接并获取 Schema
        await connector.ConnectAsync(dataSource.ConnectionString);
        var schema = await connector.GetSchemaAsync();

        // 4. 生成查询
        var generator = _registry.GetGenerator(dataSource.Type);
        var query = await generator.GenerateAsync(schema, userQuestion);

        // 5. 执行查询
        var result = await connector.ExecuteQueryAsync(query);

        // 6. 格式化结果
        return await generator.FormatResultAsync(result);
    }
}
```

### 4. 连接器示例

```csharp
public class MySqlConnector : IDataSourceConnector
{
    public async Task ConnectAsync(string connectionString)
    {
        _client = new SqlSugarClient(new ConnectionConfig
        {
            DbType = DbType.MySql,
            ConnectionString = connectionString,
            IsAutoCloseConnection = true
        });
    }

    public async Task<DatabaseSchema> GetSchemaAsync()
    {
        // 查询 INFORMATION_SCHEMA
        var tables = await _client.Ado.SqlQueryAsync<TableInfo>(...);
        return new DatabaseSchema { Tables = tables };
    }

    public async Task<QueryResult> ExecuteQueryAsync(string query)
    {
        var sw = Stopwatch.StartNew();
        var rows = await _client.Ado.SqlQueryAsync<dynamic>(query);
        sw.Stop();
        return new QueryResult { Data = rows, ExecutionTime = sw.Elapsed };
    }
}
```

## 文件数据源特殊处理

| 类型 | Schema 获取 | 查询能力 |
|------|-------------|----------|
| Excel | 读取表头行 | 全部加载后过滤 |
| CSV | 读取表头行 | 全部加载后过滤 |
| JSON | 解析结构 | 全部加载后过滤 |

## 安全性设计

### 1. 危险操作二次确认

| 操作类型 | 处理方式 |
|----------|----------|
| SELECT | 直接执行 |
| INSERT | 直接执行 |
| UPDATE | 返回生成的 SQL，需用户确认后执行 |
| DELETE | 返回生成的 SQL，需用户确认后执行 |
| DROP | 禁止执行 |

```csharp
// 查询类型判断
public enum QueryType
{
    Select,
    Insert,
    Update,
    Delete,
    Drop,
    Other
}

public class QuerySafetyChecker
{
    public QueryType GetQueryType(string query)
    {
        var trimmed = query.Trim().ToUpperInvariant();
        if (trimmed.StartsWith("SELECT")) return QueryType.Select;
        if (trimmed.StartsWith("INSERT")) return QueryType.Insert;
        if (trimmed.StartsWith("UPDATE")) return QueryType.Update;
        if (trimmed.StartsWith("DELETE")) return QueryType.Delete;
        if (trimmed.StartsWith("DROP")) return QueryType.Drop;
        return QueryType.Other;
    }

    public bool RequiresConfirmation(QueryType type)
    {
        return type == QueryType.Update || type == QueryType.Delete;
    }

    public bool IsAllowed(QueryType type)
    {
        return type != QueryType.Drop;
    }
}
```

### 2. 文件数据源路径安全

- 限制文件访问路径在指定目录内
- 防止 `../../../etc/passwd` 等路径穿越攻击

### 3. 连接字符串加密存储

- 数据库中存储加密后的连接字符串
- 运行时解密使用

## 连接管理

### 1. 连接策略

| 数据源类型 | 连接方式 | 说明 |
|------------|----------|------|
| MySQL/PostgreSQL/SQLServer/Oracle | 连接池 | SqlSugar 内置连接池 |
| SQLite/DuckDB | 每次新建 | 嵌入式数据库，轻量 |
| MongoDB | 每次新建 | 连接池开销大 |
| 文件类 | N/A | 无连接概念 |

### 2. Schema 缓存

- **缓存策略**: 首次获取后缓存到内存
- **缓存失效**: 可配置 TTL，或通过管理接口手动刷新
- **实现方式**: `IDistributedCache` 或内存缓存

```csharp
public class SchemaCache
{
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _defaultTtl = TimeSpan.FromHours(1);

    public async Task<DatabaseSchema> GetOrFetchAsync(
        long dataSourceId,
        Func<Task<DatabaseSchema>> factory,
        TimeSpan? ttl = null)
    {
        var key = $"schema:{dataSourceId}";
        return await _cache.GetOrSetAsync(key, factory, ttl ?? _defaultTtl);
    }

    public void Invalidate(long dataSourceId)
    {
        var key = $"schema:{dataSourceId}";
        _cache.Remove(key);
    }
}
```

### 3. 查询超时

- 默认超时: 30 秒（可配置）
- 不同操作可设置不同超时:
  - SELECT: 30s
  - INSERT/UPDATE/DELETE: 10s
  - 文件读取: 60s

## 确认机制

当安全等级为 `AlwaysAsk` 时，使用 `AskUser` 工具请求用户确认：

### AskUser 工具设计

```csharp
public class AskUserPlugin : ITool
{
    public string Name => "AskUser";

    public async Task<ToolResult> ExecuteAsync(AskUserRequest request)
    {
        // 返回给 Agent，要求用户确认
        return new ToolResult
        {
            NeedsUserConfirmation = true,
            Message = request.Message,
            Options = request.Options,  // ["确认执行", "取消"]
            GeneratedQuery = request.GeneratedQuery  // 待执行的 SQL/脚本
        };
    }
}
```

### 执行流程

```
用户问题 → 生成查询 → 安全检查 →
    ├── 需要确认 → AskUser 请求确认 →
    │               ├── 用户确认 → 执行查询 → 返回结果
    │               └── 用户取消 → 返回取消消息
    └── 不需要确认 → 直接执行 → 返回结果
```

### 确认超时处理

- 超时时间: 5 分钟
- 超时后: 返回超时消息，生成的查询不保存
- 用户可重新发起请求

## 产物输出策略

**原则：尽可能使用产物输出查询结果**

### 输出决策规则

| 场景 | 输出方式 | 说明 |
|------|----------|------|
| 结果 < 10 行 | 文本直接返回 | 小结果集直接展示 |
| 结果 >= 10 行 | 产物 (Artifact) | 大结果集生成文件下载 |
| 结果列包含二进制/BLOB | 产物 | 文件类型统一产物输出 |
| 复杂查询（多表关联） | 产物 | 生成 CSV/Excel 便于查看 |

### 产物类型映射

| 数据源类型 | 推荐格式 | 说明 |
|------------|----------|------|
| 关系型数据库 (MySQL/PG/SQLServer) | .csv, .xlsx | 通用格式，便于分析 |
| MongoDB | .json | 保留文档结构 |
| Excel | .xlsx | 保持原格式 |
| CSV | .csv | 保持原格式 |
| JSON | .json | 保持原格式 |

### 产物元数据

```csharp
public class QueryArtifact
{
    /// <summary>产物 ID</summary>
    public string ArtifactId { get; set; }

    /// <summary>文件名</summary>
    public string FileName { get; set; }

    /// <summary>产物类型</summary>
    public ArtifactType Type { get; set; }

    /// <summary>记录数</summary>
    public int RowCount { get; set; }

    /// <summary>文件大小 (bytes)</summary>
    public long FileSize { get; set; }

    /// <summary>下载链接</summary>
    public string Url { get; set; }

    /// <summary>预览内容 (前几行)</summary>
    public string Preview { get; set; }
}
```

### 实现示例

```csharp
public class Text2DBService
{
    private readonly IArtifactService _artifactService;

    public async Task<QueryResult> QueryAsync(long dataSourceId, string question)
    {
        var result = await ExecuteQueryAsync(...);

        // 根据结果大小决定输出方式
        if (result.RowCount >= 10 || result.HasBinaryData)
        {
            // 生成产物
            var artifact = await _artifactService.CreateArtifactAsync(
                result.Data,
                DetermineFormat(dataSourceId.Type));

            return new QueryResult
            {
                OutputType = QueryOutputType.Artifact,
                Artifact = artifact,
                Preview = GeneratePreview(result.Data, 5)
            };
        }

        // 小结果直接返回
        return new QueryResult
        {
            OutputType = QueryOutputType.Text,
            Data = result.Data
        };
    }
}
```

## 数据源执行能力

### 各数据源执行策略

| 数据源类型 | 执行能力 | 说明 |
|------------|----------|------|
| MySQL | 执行查询 | SELECT/INSERT/UPDATE/DELETE |
| PostgreSQL | 执行查询 | SELECT/INSERT/UPDATE/DELETE |
| SQLServer | 执行查询 | SELECT/INSERT/UPDATE/DELETE |
| SQLite | 执行查询 | SELECT/INSERT/UPDATE/DELETE |
| DuckDB | 执行查询 | SELECT/INSERT/UPDATE/DELETE |
| Oracle | 执行查询 | SELECT/INSERT/UPDATE/DELETE |
| MongoDB | **仅生成脚本** | 不执行，只返回生成的脚本 |
| Excel | 不支持 | 只读文件内容 |
| CSV | 不支持 | 只读文件内容 |
| JSON | 不支持 | 只读文件内容 |

## 安全等级配置

每个数据源可配置不同的安全等级：

```csharp
public enum DataSourceSecurityLevel
{
    /// <summary>始终允许 - 不需要任何确认</summary>
    AlwaysAllow = 0,

    /// <summary>始终询问 - 每次操作都需确认</summary>
    AlwaysAsk = 1,

    /// <summary>仅查询允许 - 只允许 SELECT</summary>
    QueryOnly = 2,

    /// <summary>仅读文件 - 只读取文件内容</summary>
    ReadOnly = 3
}

public class DataSource : BaseEntity
{
    /// <summary>安全等级</summary>
    public DataSourceSecurityLevel SecurityLevel { get; set; }

    /// <summary>是否允许执行写操作</summary>
    public bool AllowWrite { get; set; }

    /// <summary>是否允许执行删除操作</summary>
    public bool AllowDelete { get; set; }

    /// <summary>单次查询最大返回行数</summary>
    public int MaxResultRows { get; set; } = 1000;

    /// <summary>允许的表/集合（空表示全部）</summary>
    public string AllowedTables { get; set; }

    /// <summary>禁止的表/集合</summary>
    public string DeniedTables { get; set; }
}
```

### 安全等级行为

| 安全等级 | SELECT | INSERT | UPDATE | DELETE |
|----------|--------|--------|--------|--------|
| AlwaysAllow | 直接执行 | 直接执行 | 直接执行 | 直接执行 |
| AlwaysAsk | 直接执行 | 确认后执行 | 确认后执行 | 确认后执行 |
| QueryOnly | 直接执行 | 拒绝 | 拒绝 | 拒绝 |
| ReadOnly | 读取内容 | N/A | N/A | N/A |

## Prompt 模板设计

### 关系型数据库统一模板

关系型数据库（MySQL/PostgreSQL/SQLServer/Oracle/SQLite/DuckDB）使用统一 Prompt：

```markdown
## 数据库信息
- 类型: {DbType}
- 表结构:
{tables}

- 示例数据:
{sampleData}

## 用户问题
{question}

## 要求
1. 根据表结构和示例数据生成 SQL 查询
2. 只返回 SQL 语句，不要其他解释
3. 注意 {DbType} 语法差异（如有）
```

### Schema 获取包含示例数据

```csharp
public async Task<DatabaseSchema> GetSchemaAsync()
{
    var tables = await GetTablesAsync();

    var schema = new DatabaseSchema();
    foreach (var table in tables)
    {
        var columns = await GetColumnsAsync(table.Name);
        var sampleData = await GetSampleDataAsync(table.Name, limit: 3);

        schema.Tables.Add(new TableInfo
        {
            Name = table.Name,
            Columns = columns,
            SampleData = sampleData  // 可选，按需获取
        });
    }

    return schema;
}
```

### 类型特定调整

| 数据库 | 语法差异提示 |
|--------|-------------|
| MySQL | 使用反引号 `` ` `` 包裹表/列名 |
| PostgreSQL | 使用双引号 " " 包裹表/列名 |
| SQLServer | 使用方括号 [] 包裹表/列名 |
| Oracle | 使用双引号，PL/SQL 语法 |
| SQLite | 标准 SQL |
| DuckDB | 标准 SQL（兼容 PostgreSQL），可选：分析型特性如 TABLESAMPLE |

### 非关系型/文件特定

```markdown
## 数据源信息
- 类型: {DataSourceType}
- 集合/文件: {CollectionsOrFiles}
- 样本文档: {SampleDocuments}

## 用户问题
{question}

## 要求
生成 {TargetLanguage} 查询脚本
```

### Prompt 存储位置

```
Common/
└── Prompts/
    ├── Text2DB/
    │   ├── RelationalDb.txt      # 关系型数据库统一模板
    │   ├── MongoDb.txt          # MongoDB 模板
    │   └── File.txt             # 文件查询模板
    └── Text2DBTypes/            # 类型特定指令片段
        ├── MySQL.txt
        ├── PostgreSQL.txt
        └── ...
```

## 待定问题

1. **审计日志**
   - 是否记录所有查询操作？
   - 记录内容：用户、数据源、查询内容、执行结果、执行时间
   - 存储方式：数据库表 + 可选导出到 OSS
