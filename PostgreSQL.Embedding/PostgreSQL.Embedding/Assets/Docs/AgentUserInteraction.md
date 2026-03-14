# Agent 用户交互插件对接文档

## 概述

本文档描述前端如何对接 Agent 用户交互功能。当 Agent 执行过程中需要用户确认或选择时，会触发用户交互流程。

## 交互流程

```
1. Agent 调用 AskUser 工具
   ↓
2. 后端保存 pending tool call (Status=0)
   ↓
3. 后端发送 tool_use 事件 → 前端渲染交互 UI
   ↓
4. 用户点击选项 → 前端调用提交响应 API
   ↓
5. 后端更新 tool call (Status=1, Output=用户选择)
   ↓
6. InteractionPlugin 检测到变化，返回结果给 Agent
   ↓
7. Agent 继续执行
```

## SSE 事件监听

### 监听 tool_use 事件

前端需要通过 SSE 连接监听后端的流式事件。当 Agent 调用 `AskUser` 工具时，会收到 `tool_use` 类型的事件。

**事件格式**：

```typescript
interface ToolUseEvent {
  type: "tool_use";
  id: string;           // tool call ID，用于提交响应
  name: "InteractionPlugin.AskUser";
  input: {
    request: {
      Mode: "approve" | "choice";  // approve=批准, choice=选择
      Question: string;             // 询问的问题
      Options: string[];           // 选项列表
      MultiSelect: boolean;        // 是否允许多选
      IsPending: boolean;          // 是否等待用户响应
      PendingMessage: string;      // 等待提示信息
    }
  };
}
```

**示例**：

```javascript
const eventSource = new EventSource('/api/chat/stream?conversationId=xxx');

eventSource.addEventListener('tool_use', (event) => {
  const data = JSON.parse(event.data);
  console.log('收到交互请求:', data);

  // data.id 是 tool call ID，提交响应时需要用到
  // data.input.request 包含交互详情
});
```

## 交互 UI 渲染

### approve 模式（批准）

显示确认/拒绝按钮：

```jsx
function ApproveUI({ request, toolCallId, onSubmit }) {
  return (
    <div className="interaction-approve">
      <p>{request.Question}</p>
      <div className="options">
        {request.Options.map((option) => (
          <button key={option} onClick={() => onSubmit(toolCallId, [option])}>
            {option}
          </button>
        ))}
      </div>
    </div>
  );
}
```

### choice 模式（选择）

显示选项列表，支持单选或多选：

```jsx
function ChoiceUI({ request, toolCallId, onSubmit }) {
  const [selected, setSelected] = useState([]);

  const handleToggle = (option) => {
    if (request.MultiSelect) {
      // 多选
      setSelected(prev =>
        prev.includes(option)
          ? prev.filter(o => o !== option)
          : [...prev, option]
      );
    } else {
      // 单选
      setSelected([option]);
    }
  };

  return (
    <div className="interaction-choice">
      <p>{request.Question}</p>
      <div className="options">
        {request.Options.map((option) => (
          <button
            key={option}
            className={selected.includes(option) ? 'selected' : ''}
            onClick={() => handleToggle(option)}
          >
            {option}
          </button>
        ))}
      </div>
      <button
        disabled={selected.length === 0}
        onClick={() => onSubmit(toolCallId, selected)}
      >
        确认
      </button>
    </div>
  );
}
```

## 提交用户响应

### API 接口

```
POST /api/traces/{messageId}/toolcalls/{toolCallId}/respond
```

**请求头**：

```
Content-Type: application/json
```

**请求体**：

```typescript
interface SubmitRequest {
  SelectedOptions: string[];  // 用户选择的选项列表
}
```

**响应**：

```typescript
// 成功
{
  "code": 200,
  "data": true,
  "message": "success"
}

// 失败 - Tool call 不存在
{
  "code": 500,
  "data": null,
  "message": "Tool call not found"
}

// 失败 - 已经响应过
{
  "code": 500,
  "data": null,
  "message": "Already responded"
}
```

**示例**：

```javascript
async function submitUserResponse(messageId, toolCallId, selectedOptions) {
  const response = await fetch(
    `/api/traces/${messageId}/toolcalls/${toolCallId}/respond`,
    {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({
        SelectedOptions: selectedOptions
      })
    }
  );

  const result = await response.json();
  return result;
}
```

## 完整示例

```jsx
import { useState, useEffect } from 'react';

function UserInteractionHandler({ eventSource, messageId }) {
  const [pendingRequests, setPendingRequests] = useState([]);

  useEffect(() => {
    // 监听 tool_use 事件
    eventSource.addEventListener('tool_use', (event) => {
      const data = JSON.parse(event.data);

      if (data.name === 'InteractionPlugin.AskUser') {
        setPendingRequests(prev => [...prev, {
          toolCallId: data.id,
          request: data.input.request
        }]);
      }
    });

    return () => {
      eventSource.removeEventListener('tool_use', null);
    };
  }, [eventSource]);

  const handleSubmit = async (toolCallId, selectedOptions) => {
    await submitUserResponse(messageId, toolCallId, selectedOptions);
    setPendingRequests(prev => prev.filter(r => r.toolCallId !== toolCallId));
  };

  return (
    <div>
      {pendingRequests.map(({ toolCallId, request }) => (
        request.Mode === 'approve' ? (
          <ApproveUI
            key={toolCallId}
            request={request}
            toolCallId={toolCallId}
            onSubmit={handleSubmit}
          />
        ) : (
          <ChoiceUI
            key={toolCallId}
            request={request}
            toolCallId={toolCallId}
            onSubmit={handleSubmit}
          />
        )
      ))}
    </div>
  );
}
```

## 错误处理

### 超时

后端默认超时时间为 300 秒（5 分钟）。超时后 Agent 会收到 `"超时"` 作为选择结果，前端可以正常结束交互。

### 重复提交

如果用户已经提交过响应，后端会返回 `"Already responded"` 错误，前端应该禁用提交按钮或显示提示。

## 配置说明

### 后端配置（InteractionPlugin）

| 配置项 | 默认值 | 说明 |
|--------|--------|------|
| PollIntervalMs | 1000 | 轮询间隔（毫秒） |
| DefaultTimeoutSeconds | 300 | 默认超时时间（秒） |

如需修改超时时间，可以在 `AskUser` 调用时传入 `timeoutSeconds` 参数（需要先在插件中添加此参数支持）。
