# Loop Engineering 资源整理

> **Loop Engineering（循环工程）** 是 2026 年兴起的 AI Agent 开发新范式，指的是设计、运营和改进反馈循环，让 AI 编码代理能够规划工作、修改代码、观察结果并反复优化，直到软件任务完成。

## 📌 核心概念

### 什么是 Loop Engineering？

**Loop Engineering** 的核心理念是：**不再直接提示（prompt）AI 代理，而是设计循环（loop）来提示代理**。

- **传统方式**：人类直接向 AI 模型发出指令，获得一次性响应
- **循环工程**：设计自动化循环系统，让 AI 代理自主执行"观察-决策-行动-验证"的迭代过程

> "You should be designing loops that prompt your agents."  
> —— **Peter Steinberger**，OpenClaw 创始人

> "I don't prompt Claude anymore."  
> —— **Boris Cherny**，Anthropic Claude Code 负责人

### 循环的五大构建模块

根据 Addy Osmani 的总结，一个完整的 Agent Loop 包含：

| 模块 | 说明 |
|------|------|
| **Automations（自动化）** | 定期触发的发现和分诊任务 |
| **Worktrees（工作树）** | 隔离的工作环境，避免冲突 |
| **Skills（技能）** | 编码过程而非知识的轻量级能力模块 |
| **Connectors（连接器）** | 与外部工具和系统的集成接口 |
| **Sub-agents（子代理）** | 专门执行特定任务的嵌套代理 |

**+ Memory（记忆脊柱）**：贯穿整个循环的上下文记忆系统

---

## 📚 核心资源

### 1. 权威文章

#### Addy Osmani - Loop Engineering
- **链接**：https://addyosmani.com/blog/loop-engineering/
- **作者**：Addy Osmani（Google Chrome 工程团队）
- **内容**：Loop Engineering 的权威定义和实践指南，详细介绍了五大构建模块及在 Claude Code / Codex 中的实现方式
- **推荐度**：⭐⭐⭐⭐⭐ 必读

#### Kilo.ai - What Is Loop Engineering?
- **链接**：https://kilo.ai/articles/what-is-loop-engineering
- **内容**：系统性介绍 AI 编码反馈循环的工作原理，以及团队如何使用迭代代理工作流
- **推荐度**：⭐⭐⭐⭐

#### MindStudio - What Is Loop Engineering? The New Meta for AI Coding Agents
- **链接**：https://www.mindstudio.ai/blog/what-is-loop-engineering-ai-coding-agents
- **内容**：深入解析循环工程的底层机制，适用于从零构建代理或使用现成工具的开发者
- **推荐度**：⭐⭐⭐⭐

#### Lushbinary - Loop Engineering: The Guide for AI Agents
- **链接**：https://lushbinary.com/blog/loop-engineering-ai-coding-agents-guide
- **内容**：完整指南，涵盖循环工程与提示工程、上下文工程的区别，以及失败模式分析
- **推荐度**：⭐⭐⭐⭐

### 2. 视频教程

#### Owain Lewis - Loop Engineering: How To Build Autonomous AI Agents
- **链接**：https://www.youtube.com/watch?v=RVEaDvh6f5A
- **内容**：完整实战指南，展示如何用 Claude Code 和 Codex 构建自动化开发工作流
- **亮点**：
  - Manager Loop：自主分类和标记待办事项
  - Worker Loop：将就绪任务转化为 Pull Request
  - 代理间如何相互提示
  - 安全防护措施和评估方法
- **配套代码**：https://github.com/owainlewis/youtube-tutorials/tree/main/tutorials/loop-engineering
- **推荐度**：⭐⭐⭐⭐⭐

#### 01Coder - Loop Engineering 从理论到实践
- **链接**：https://www.youtube.com/watch?v=WuMlsfKeWHc
- **内容**：中文讲解，用 Boris Cherny 的"三阶段"理论梳理 Loop Engineering 的转变
- **亮点**：包含两个实际 loop 演示（最小化热身 + 自动内容流水线）
- **推荐度**：⭐⭐⭐⭐（中文友好）

#### What's AI - Loop Engineering: The New Way to Use Claude Code & Codex
- **链接**：https://www.youtube.com/watch?v=NjXIIH9vcv0
- **内容**：解析五大构建模块，以及两个致命陷阱：模糊目标和失控的 Token 成本
- **推荐度**：⭐⭐⭐⭐

### 3. 社区讨论

#### Reddit - So is "loop engineering" the next AI dev buzzword?
- **链接**：https://www.reddit.com/r/myclaw/comments/1u047p8/so_is_loop_engineering_the_next_ai_dev_buzzword
- **内容**：社区对 Loop Engineering 是否为炒作的讨论，包含 Peter Steinberger 和 Boris Cherny 的原始发言截图
- **价值**：了解概念起源和争议

#### LinkedIn - Loop Engineering: AI Hype or Real Automation?
- **链接**：https://www.linkedin.com/posts/mortenrandhendriksen_loops-loop-engineering-the-ai-hype-machine-activity-7470167326194196480-xh7u
- **内容**：动态工作流的另一种视角，Claude 编写编排脚本启动并行子代理

---

## 🛠️ 实践工具和平台

### Claude Code（Anthropic）
- **官网**：https://docs.anthropic.com/claude-code
- **Loop 支持**：
  - `/loop` 命令：定时运行提示或命令
  - `/goal` 命令：运行直到完成目标
  - Hooks：在代理生命周期的特定点触发 Shell 命令
  - Skills：轻量级能力模块索引
- **特点**：原生支持循环工程模式

### OpenAI Codex
- **官网**：https://developers.openai.com/codex
- **Loop 支持**：
  - Automations 标签：选择项目、提示、频率、环境
  - 结果进入 Triage 收件箱
  - 与 GitHub Actions 集成
- **特点**：云端执行，支持后台自动化

### Coze Loop（字节跳动开源）
- **GitHub**：https://github.com/coze-dev/coze-loop
- **简介**：面向开发者的 AI 智能体优化平台，提供从开发、调试、评估到监控的全生命周期管理
- **核心功能**：
  - Prompt 开发与调试优化
  - 评估测试与监控观测
  - 部署运维与团队协作
- **技术栈**：Go + React
- **推荐度**：⭐⭐⭐⭐（企业级方案）

### Coze Studio（字节跳动开源）
- **GitHub**：https://github.com/coze-dev/coze-studio
- **简介**：Agent 开发平台，包含完整的工作流引擎和插件系统
- **特点**：Apache 2.0 协议，可商用

### Eino（字节跳动开源）
- **GitHub**：https://github.com/cloudwego/eino
- **简介**：基于 Go 语言的 Agent 开发框架
- **特点**：提供 Graph、Chain、Workflow 多种编排方式

---

## 🔗 关键人物

| 人物 | 身份 | 贡献 |
|------|------|------|
| **Addy Osmani** | Google Chrome 工程团队 | 撰写 Loop Engineering 权威定义文章 |
| **Peter Steinberger** | OpenClaw 创始人 | 发起"设计循环而非提示"的讨论 |
| **Boris Cherny** | Anthropic Claude Code 负责人 | 提出"我不再提示 Claude"的理念 |
| **Owain Lewis** | AI 工程师/教育者 | 制作完整的 Loop Engineering 实战教程 |

---

## 📊 应用场景

### 1. 自动化代码审查
- Manager Loop 分类待办事项（按风险、类型、是否适合代理处理）
- Worker Loop 生成代码 → 子代理审查差异 → 运行检查 → 提交 PR

### 2. 持续集成/持续部署（CI/CD）
- 监控代码变更 → 自动触发测试 → 修复失败 → 重新部署
- 与 GitHub Actions 等 CI/CD 工具深度集成

### 3. 技术债务清理
- 扫描代码库识别问题 → 按优先级排序 → 自动修复 → 验证结果
- 支持多代理并行处理不同模块

### 4. 文档自动化
- 分析代码变更 → 生成/更新文档 → 审查准确性 → 发布
- 支持多语言文档同步

### 5. 内容创作流水线
- 选题策划 → 内容生成 → 质量评估 → 优化迭代
- 可配置子代理分别负责不同环节

---

## ⚠️ 注意事项

### 何时不需要 Loop Engineering

根据社区讨论，以下情况可能不需要循环工程：

1. **简单的一次性任务**：直接提示更高效
2. **Token 预算有限**：循环会消耗大量 Token
3. **需要精确控制输出**：循环的自主性可能带来不可预测性
4. **团队缺乏 AI 工程经验**：学习曲线较陡

### 四条判断标准（来自 01Coder）

考虑是否使用 Loop Engineering 时，问自己：

1. 任务是否**重复性高**？
2. 任务是否可以**明确验证**成功标准？
3. 是否有**足够的 Token 预算**支持迭代？
4. 是否能接受**一定的自主性**和**可能的失败**？

---

## 🎓 学习路径

### 初学者
1. 阅读 Addy Osmani 的 [Loop Engineering](https://addyosmani.com/blog/loop-engineering/) 文章
2. 观看 01Coder 的[中文讲解视频](https://www.youtube.com/watch?v=WuMlsfKeWHc)
3. 在 Claude Code 中尝试 `/loop` 和 `/goal` 命令

### 进阶者
1. 学习 Owain Lewis 的[完整实战教程](https://www.youtube.com/watch?v=RVEaDvh6f5A)
2. 研究 [Coze Loop](https://github.com/coze-dev/coze-loop) 的架构设计
3. 设计自己的 Manager-Worker Loop 系统

### 高级用户
1. 探索多代理协作和嵌套子代理模式
2. 构建自定义的 Skills 和 Connectors
3. 集成评估和监控系统
4. 研究失败模式和防护措施

---

## 📖 相关概念对比

| 概念 | 定义 | 与 Loop Engineering 的关系 |
|------|------|---------------------------|
| **Prompt Engineering** | 设计单次提示以获得最佳响应 | Loop 中的每个步骤都需要好的提示 |
| **Context Engineering** | 管理代理可访问的上下文信息 | 为循环提供必要的背景信息 |
| **Agent Harness Engineering** | 构建代理运行的环境 | 循环运行的基础设施 |
| **Vibe Coding** | 用自然语言描述需求让 AI 生成代码 | Loop Engineering 的前身/简化版 |
| **Agentic Coding** | 让 AI 代理自主完成编码任务 | Loop Engineering 是实现方式之一 |

---

## 🔮 发展趋势

### 2026 年 6 月现状
- Loop Engineering 从概念炒作进入实践阶段
- Claude Code、Codex 等工具原生支持循环模式
- 开源社区开始提供企业级解决方案（如 Coze Loop）

### 未来展望
- **Fleet Engineering**：设计管理多个循环的系统（Peter Steinberger 预测）
- **自适应循环**：根据任务复杂度自动调整循环策略
- **跨平台标准化**：统一的循环描述语言和互操作性
- **成本优化**：更智能的 Token 管理和缓存策略

---

## 🇨🇳 中文社区资源

### 知乎专栏

#### Loop Engineering 深度解析与实战指南（全网最全）
- **链接**：https://zhuanlan.zhihu.com/p/2048317502342666078
- **作者**：徐小夕
- **内容**：系统梳理 AI 编程的三次革命：Prompt Engineering → Context Engineering → Loop Engineering，详解六大核心要素及 Claude Code / Codex 中的实现
- **推荐度**：⭐⭐⭐⭐⭐ 中文必读

#### Loop Engineering 循环工程又是什么鬼？
- **链接**：https://zhuanlan.zhihu.com/p/2047996686807589866
- **作者**：鹤啸九天
- **内容**：用图解方式解析 Loop Engineering 的核心概念，对比 Harness Engineering 的区别
- **推荐度**：⭐⭐⭐⭐

#### AI工程范式的三次演化：Prompt Engineering → Context Engineering → Harness Engineering
- **链接**：https://zhuanlan.zhihu.com/p/2015142041282163260
- **作者**：Weyne Chen
- **内容**：从更宏观的视角理解 Loop Engineering 在 AI 工程演进中的位置
- **推荐度**：⭐⭐⭐⭐

### B站视频

#### 【Loop Engineering 循环工程】从理论到实践，它真的适合每个人吗？
- **链接**：https://www.bilibili.com/video/BV1M2Jj6yE5x
- **作者**：01Coder（SmallWoods）
- **内容**：中文视频讲解，用 Boris Cherny 的"三阶段"理论梳理转变，包含两个实际 loop 演示
- **亮点**：
  - 最小化热身：用 `/goal` 让递归目标变具体
  - 主菜：每天自动攒选题的内容流水线
  - 泼冷水：多数人其实还不需要 loop
- **推荐度**：⭐⭐⭐⭐⭐ 中文视频首选

### 腾讯新闻

#### Loop Engineering 循环工程又是什么鬼？
- **链接**：https://view.inews.qq.com/a/20260610A03P4U00
- **内容**：快速了解 Loop Engineering 的核心概念和五大模块
- **推荐度**：⭐⭐⭐ 快速入门

---

## 📝 总结

**Loop Engineering 不是银弹，而是一种思维方式的转变**：

- ❌ 不要：把 AI 当作一次性代码生成器
- ✅ 要做：设计系统让 AI 持续迭代和改进

**核心公式**：
```
目标 → 观察 → 决策 → 行动 → 验证 → (循环/停止)
```

**适用场景**：重复性高、可验证、有足够 Token 预算的任务

**关键资源**：
- 📖 [Addy Osmani - Loop Engineering](https://addyosmani.com/blog/loop-engineering/)
- 🎥 [Owain Lewis 实战教程](https://www.youtube.com/watch?v=RVEaDvh6f5A)
- 🇨🇳 [徐小夕 - 中文深度解析](https://zhuanlan.zhihu.com/p/2048317502342666078)
- 💻 [Coze Loop 开源项目](https://github.com/coze-dev/coze-loop)

---

*文档整理时间：2026 年 6 月*  
*数据来源：Google、YouTube、GitHub、Reddit、LinkedIn、知乎、B站、腾讯新闻等公开资源*