# 数据源管理 API 文档

## 概述

数据源管理接口，用于管理应用下的数据库连接配置。

## 数据模型

### DataSource 实体

| 字段 | 类型 | 说明 |
|------|------|------|
| Id | long | 主键 ID |
| Name | string | 数据源名称 |
| Type | DataSourceType | 数据源类型 |
| ConnectionString | string | 连接字符串 |
| Description | string | 描述 |
| AppId | long | 所属应用 ID |
| IsEnabled | bool | 是否启用 |
| CreatedAt | DateTime | 创建时间 |
| UpdatedAt | DateTime | 更新时间 |

### DataSourceType 枚举

| 值 | 说明 |
|----|------|
| 1 | MySQL |
| 2 | PostgreSQL |
| 3 | SQL Server |
| 4 | Oracle |
| 5 | SQLite |
| 6 | DuckDB |
| 10 | MongoDB |
| 20 | Excel |
| 21 | CSV |
| 22 | JSON |

## API 接口

### 1. 分页查询数据源列表

```
GET /api/llmapp/{appId}/datasources/paginate?pageIndex=1&pageSize=10
```

**响应示例：**
```json
{
  "success": true,
  "data": {
    "rows": [
      {
        "id": 1,
        "name": "MySQL 数据库",
        "type": 1,
        "connectionString": "Server=localhost;Database=test;Uid=root;Pwd=***",
        "description": "测试数据库",
        "appId": 1,
        "isEnabled": true,
        "createdAt": "2024-01-01T00:00:00Z",
        "updatedAt": "2024-01-01T00:00:00Z"
      }
    ],
    "totalCount": 1
  }
}
```

---

### 2. 获取所有启用的数据源

```
GET /api/llmapp/{appId}/datasources
```

**响应示例：**
```json
{
  "success": true,
  "data": [
    {
      "id": 1,
      "name": "MySQL 数据库",
      "type": 1,
      "connectionString": "Server=localhost;Database=test;Uid=root;Pwd=***",
      "description": "测试数据库",
      "appId": 1,
      "isEnabled": true
    }
  ]
}
```

---

### 3. 获取单个数据源详情

```
GET /api/llmapp/{appId}/datasources/{id}
```

**响应示例：**
```json
{
  "success": true,
  "data": {
    "id": 1,
    "name": "MySQL 数据库",
    "type": 1,
    "connectionString": "Server=localhost;Database=test;Uid=root;Pwd=***",
    "description": "测试数据库",
    "appId": 1,
    "isEnabled": true,
    "createdAt": "2024-01-01T00:00:00Z",
    "updatedAt": "2024-01-01T00:00:00Z"
  }
}
```

---

### 4. 新增数据源

```
POST /api/llmapp/{appId}/datasources
```

**请求体：**
```json
{
  "name": "MySQL 数据库",
  "type": 1,
  "connectionString": "Server=localhost;Database=test;Uid=root;Pwd=123456",
  "description": "测试数据库"
}
```

**响应示例：**
```json
{
  "success": true,
  "data": {
    "id": 1,
    "name": "MySQL 数据库",
    "type": 1,
    "connectionString": "Server=localhost;Database=test;Uid=root;Pwd=123456",
    "description": "测试数据库",
    "appId": 1,
    "isEnabled": true,
    "createdAt": "2024-01-01T00:00:00Z",
    "updatedAt": "2024-01-01T00:00:00Z"
  }
}
```

---

### 5. 更新数据源

```
PUT /api/llmapp/{appId}/datasources/{id}
```

**请求体：**
```json
{
  "name": "MySQL 数据库(更新)",
  "type": 1,
  "connectionString": "Server=localhost;Database=test2;Uid=root;Pwd=123456",
  "description": "更新后的描述"
}
```

**响应示例：**
```json
{
  "success": true,
  "data": {
    "id": 1,
    "name": "MySQL 数据库(更新)",
    "type": 1,
    "connectionString": "Server=localhost;Database=test2;Uid=root;Pwd=123456",
    "description": "更新后的描述",
    "appId": 1,
    "isEnabled": true,
    "createdAt": "2024-01-01T00:00:00Z",
    "updatedAt": "2024-01-02T00:00:00Z"
  }
}
```

---

### 6. 删除数据源

```
DELETE /api/llmapp/{appId}/datasources/{id}
```

**响应示例：**
```json
{
  "success": true,
  "data": null
}
```

---

### 7. 启用/禁用数据源

```
PUT /api/llmapp/{appId}/datasources/{id}/toggle
```

**响应示例：**
```json
{
  "success": true,
  "data": {
    "id": 1,
    "name": "MySQL 数据库",
    "type": 1,
    "connectionString": "Server=localhost;Database=test;Uid=root;Pwd=***",
    "description": "测试数据库",
    "appId": 1,
    "isEnabled": false,
    "createdAt": "2024-01-01T00:00:00Z",
    "updatedAt": "2024-01-02T00:00:00Z"
  }
}
```

## 连接字符串示例

### MySQL
```
Server=localhost;Port=3306;Database=mydb;Uid=root;Pwd=123456
```

### PostgreSQL
```
Host=localhost;Port=5432;Database=mydb;Username=postgres;Password=123456
```

### SQL Server
```
Server=localhost;Database=mydb;User Id=sa;Password=123456;TrustServerCertificate=True
```

### MongoDB
```
mongodb://localhost:27017/mydb
```

### SQLite
```
Data Source=mydb.db
```

### DuckDB
```
Data Source=mydb.db
```

## 前端实现建议

### 1. 列表页面
- 支持分页查询
- 显示数据源名称、类型、描述、启用状态
- 提供新增、编辑、删除、启用/禁用操作

### 2. 新增/编辑弹窗
- 表单字段：名称、类型（下拉选择）、连接字符串、描述
- 类型选择后显示对应的连接字符串格式提示
- 连接字符串建议使用密码输入框（隐藏）

### 3. 类型下拉选项
```javascript
const dataSourceTypes = [
  { value: 1, label: 'MySQL' },
  { value: 2, label: 'PostgreSQL' },
  { value: 3, label: 'SQL Server' },
  { value: 4, label: 'Oracle' },
  { value: 5, label: 'SQLite' },
  { value: 6, label: 'DuckDB' },
  { value: 10, label: 'MongoDB' },
  { value: 20, label: 'Excel' },
  { value: 21, label: 'CSV' },
  { value: 22, label: 'JSON' }
];
```

### 4. 权限控制
- 只允许操作当前用户所属应用的数据源
- AppId 从当前登录会话获取
