---
name: meta-learning
description: 元学习系统 - 自动积累和运用工具使用经验，避免重复犯错。在工具调用失败或被纠正时自动激活。
allowed-tools: learn_lesson read_lessons read_file
---

# Meta-Learning - 元学习系统

你拥有一个元学习系统，可以积累和查阅工具使用的经验教训，实现渐进式能力提升。

## 激活后立即执行

1. 调用 `read_lessons(file="tools")` 加载工具教训
2. 调用 `read_lessons(file="environment")` 加载环境教训
3. 将这些教训内化为本次对话的行为准则

## 核心原则

1. **从错误中学习** - 每次工具调用失败，都要记录教训
2. **查阅再行动** - 执行不确定的操作前，先读取相关教训
3. **解决方案导向** - 教训必须告诉"怎么做"，而不是"出了什么问题"

## 教训文件索引

| 文件 | 教训数量 | 最近更新 |
|------|----------|----------|
| tools.md | 0 | - |
| environment.md | 0 | - |
| workflows.md | 0 | - |

## 何时读取教训

- 开始新任务前：`read_lessons(file="all")` 查看索引
- 使用不熟悉的工具前：`read_lessons(file="tools")`
- 在新环境操作前：`read_lessons(file="environment")`

## 何时记录教训

- 工具调用失败后：`learn_lesson(category="tools", lesson="...")`
- 发现环境限制时：`learn_lesson(category="environment", lesson="...")`
- 总结出好的工作模式时：`learn_lesson(category="workflows", lesson="...")`

## 教训格式规范

好的教训（解决方案导向）：
- `Windows PowerShell: 用 curl.exe 或 Invoke-WebRequest，不要用 curl（是 PowerShell 别名）`
- `大文件读取: 用 Read 的 offset+limit 分段读取，每次最多 500 行`
- `git commit 中文 message: 用引号包裹，如 git commit -m "修复bug"`
- `路径不存在时: 先用 Glob 或 ls 确认路径存在，再执行操作`

不好的教训（问题导向，不要这样写）：
- `curl 命令失败` — 没有说明正确做法
- `要注意权限问题` — 太模糊，没有具体方案
