# 多行输入与折叠粘贴设计

本文说明 InsightaAI CLI 的多行输入组件如何处理键盘输入、终端控制序列和大段粘贴。目标读者是维护 CLI 交互层的开发者；阅读本文后，应能先理解状态模型，再阅读 `MultiLineTextPrompt`、`PromptInputBuffer` 和 `Terminal` 中的实现。

相关代码：

| 文件 | 职责 |
| --- | --- |
| `src/InsightaAI.Agent.Cli/UI/MultiLineTextPrompt.cs` | 读取按键、识别粘贴、重绘编辑区、提交输入 |
| `src/InsightaAI.Agent.Cli/UI/PromptInputSource.cs` | 定义通用输入事件、VT 解析与输入源选择工厂 |
| `src/InsightaAI.Agent.Cli/UI/WindowsVtPromptInputSource.cs` | Windows 优先路径：启用 VT input 与 Win32 Input Mode，解析按键记录和 bracketed paste |
| `src/InsightaAI.Agent.Cli/UI/WindowsConsolePromptInputSource.cs` | Windows 回退路径：从 Console 按键事件读取普通输入和可用的粘贴边界 |
| `src/InsightaAI.Agent.Cli/UI/PromptInputBuffer.cs` | 保存“真实文本”和“编辑态显示文本” |
| `src/InsightaAI.Agent.Cli/UI/Terminal.cs` | 判断终端能力与封装基础 VT 操作 |
| `src/InsightaAI.Agent.Cli/UI/ChatRenderer.cs` | 在普通终端使用多行输入；Git Bash 走安全的单行降级 |

### 输入源选择顺序

`PromptInputSourceFactory` 按能力选择底层输入源，Chat 层只消费统一的 `PromptKeyInputEvent` / `PromptPasteInputEvent`：

1. Windows 首先尝试 `WindowsVtPromptInputSource`。它将 Console 切换到 `ENABLE_VIRTUAL_TERMINAL_INPUT`，并使用 `ESC[?9001h` 请求终端将 Win32 键盘记录编码为 VT 序列；这使 Shift/Ctrl+Enter 的修饰键与 bracketed paste 边界能在同一字节流中传递。
2. 若 VT 输入模式无法启用，回退到 `WindowsConsolePromptInputSource`，保留普通按键输入能力。
3. 非 Windows 或上述原生路径不可用时，使用通用 `ConsolePromptInputSource`，解析标准 VT 按键与 bracketed paste 序列。

所有会修改终端模式的输入源都必须在 `Dispose` / `finally` 中恢复原始 Console mode，并关闭 Win32 Input Mode 与 bracketed paste。

## 1. 要解决的问题

普通 `Console.ReadLine()` 适合一行、一次提交的命令式输入，但不适合 Agent 对话：用户经常从编辑器、网页或终端复制一段 Markdown、代码、日志或报错信息。该段文本可能包含数十行、中文、emoji 和空行。

如果逐字符直接回显，存在三个问题：

1. 首个换行可能被当成“发送”，导致粘贴内容被截断。
2. 数千字符在终端中立即铺开，用户看不到输入边界，也难以在发送前删掉整段内容。
3. 终端不是统一 GUI 控件。Windows Console、Windows Terminal、ConHost、xterm、mintty、SSH 与 IDE 内置终端，对输入和 ANSI/VT 序列的处理不同。

因此输入组件需要同时满足：

- 手动 Enter 发送；Shift+Enter、Ctrl+Enter 插入换行。
- 多行粘贴不在第一个换行处发送。
- 编辑时将大段粘贴压缩为可读摘要，例如 `[pasted 1,368 characters]`。
- 发送时恢复完整原文：LLM、会话持久化、标题生成和终端历史都不能看到摘要文本。
- 用户可将粘贴内容作为一个整体移动、删除，而不必进入数千字符的行内编辑。
- 不支持高级终端能力时安全退化，而不是把控制字符显示到屏幕上。

## 2. 两种文本：真实文本与显示文本

这套设计最重要的原则是：**编辑态展示内容不等于提交内容**。

`PromptInputBuffer` 不再只保存一个 `StringBuilder`，而是保存有序的输入单元（`Entry`）：

```text
Entry = (RawText, DisplayText)
```

普通键盘字符的两个字段相同：

```text
RawText:     hello
DisplayText: hello
```

粘贴块的字段不同：

```text
RawText:     真实的完整多行文本……
DisplayText: [pasted 1,368 characters]
```

缓冲区的关键派生值如下：

| 属性 | 用途 |
| --- | --- |
| `Text` | 将所有 `RawText` 连接；唯一允许传给 Agent、会话、标题服务的值 |
| `DisplayText` | 将所有 `DisplayText` 连接；只用于编辑态重绘 |
| `DisplayTextBeforeCaret` | 从编辑区起点计算光标行列时使用 |
| `ContainsPaste` | 判断提交前是否需要把摘要替换为原文 |

例如，用户输入 `请分析：` 后粘贴一段日志，再输入 `重点看异常。`，缓冲区可以表示为：

```text
┌───────────┬──────────────────────────────┐
│ RawText   │ DisplayText                  │
├───────────┼──────────────────────────────┤
│ 请分析：   │ 请分析：                      │
│ <完整日志> │ [pasted 1,368 characters]   │
│ 重点看异常。│ 重点看异常。                  │
└───────────┴──────────────────────────────┘
```

编辑时显示：

```text
> 请分析：[pasted 1,368 characters]重点看异常。
```

提交时返回：

```text
请分析：<完整日志>重点看异常。
```

摘要字符串绝不会混入 `Text`；它不是传输协议的一部分，也不应进入 Agent 上下文。

## 3. 输入单元与光标语义

光标位置不再是“第几个 UTF-16 `char`”，而是“位于第几个 `Entry` 前”。普通输入通常每个字符是一个 Entry；一个 emoji 代理对会作为一个 Entry 插入；一次粘贴无论有多少字符都只占一个 Entry。

这种设计让粘贴块成为原子单元：

| 操作 | 光标在粘贴块前 | 光标在粘贴块后 |
| --- | --- | --- |
| `RightArrow` | 一次跨到块后 | 无变化或继续右移 |
| `LeftArrow` | 无变化或继续左移 | 一次跨到块前 |
| `Delete` | 删除整个粘贴块 | 删除右侧单元 |
| `Backspace` | 删除左侧单元 | 删除整个粘贴块 |

删除一个粘贴块会同时删除其 `RawText` 和 `DisplayText`，因此不会发生“屏幕上删了，但发送时仍带着原文”的问题。

Home/End 基于 `DisplayText` 的换行移动。这是有意的：编辑阶段粘贴内容是单行摘要，用户操作的是当前可见的编辑模型；粘贴内部的真实换行只在提交后展开。

## 4. 编辑态重绘

`MultiLineTextPrompt` 不能依赖 `Console.CursorLeft`、`Console.CursorTop` 或 `Console.SetCursorPosition`。在 Git Bash 的 mintty/winpty 组合下，这些 Win32 Console API 可能抛 `IOException`，或返回不可靠坐标。

因此组件使用 VT 光标序列维护一个编辑区锚点：

| 序列 | 含义 |
| --- | --- |
| `\u001B[{n}A` | 向上移动 `n` 行，回到编辑区首行 |
| `\u001B[K` | 清除从当前光标到行尾 |
| `\u001B[{n}B` | 向下移动 `n` 行 |
| `\u001B[{n}C` | 向右移动 `n` 列 |

注意 C# 转义写法必须使用 `\u001B`，而不能写成 `\x1b7`：C# 的 `\x` 会继续吞后续十六进制字符，`\x1b7` 实际是 Unicode `U+01B7`（`Ʒ`），并非 ESC 再跟数字 `7`。

一次重绘的逻辑为：

1. 用回车回到当前行第 0 列，再依据组件维护的相对行号向上移动，回到锚点。
2. 将旧编辑区每一物理行清空。
3. 根据“终端宽度减提示符宽度”折行，写出 `DisplayText`；首行保留真实提示符 `> `，所有后续物理行补等宽空格作为缩进。
4. 从 `DisplayTextBeforeCaret` 计算光标应处的物理行与列。
5. 若目标位于首行，直接向右移动；若目标位于后续物理行，先输出 `\r` 回到第 0 列，再向下移动，并跳过提示符等宽的缩进后向右移动。因为首行从提示符后的锚点开始，而显式换行和自动折行后的行从第 0 列开始，不能把提示符宽度叠加到后续行。

显示宽度不是 `string.Length`。组件对常见 CJK 全角字符按 2 列、TAB 按 8 列计算；emoji 代理对按一个宽字符处理，避免在折行、左右移动或删除时拆开代理对。该宽度计算仍是简化实现，不是完整的 Unicode `wcwidth` 表，详见“已知限制”。

## 5. 如何识别粘贴

终端不存在一个所有平台统一可用的“这是粘贴”API，因此采用两层策略。

### 5.1 首选：Bracketed Paste 协议

支持该协议的终端会在粘贴内容外包裹边界标记。提示开始时，程序写出：

```text
ESC[?2004h
```

终端随后会把粘贴输入表示为：

```text
ESC[200~<原始粘贴内容>ESC[201~
```

提示结束（提交、取消、异常或 EOF）时，程序必须写出：

```text
ESC[?2004l
```

在该路径中，`ConsolePromptInputSource` 会：

1. 收到 ESC 后验证后续 `[200~` 起始标记。
2. 连续读取粘贴正文，直到 `ESC[201~` 结束标记。
3. 将 `\r\n` 和孤立 `\r` 统一为 `\n`。
4. 用 `InsertPaste` 插入一个原子粘贴块并触发一次重绘。

如果 ESC 后并不是起始标记，已经读取的字符会放回输入源的待处理队列，由常规按键逻辑处理；不能因为误判吞掉用户输入。

该路径在能把原始 VT 输入交给应用的 xterm 类终端中最可靠。`Terminal.SupportsBracketedPaste` 仅在输入、输出均未重定向且 `TERM` 不是 `dumb` 时尝试启用；它是保守的“可尝试”判断，而不是功能探测的数学证明。

### 5.2 无协议边界时：安全降级

实践中，Windows 的 `Console.ReadKeyAsync` 可能经过 Win32 Console 输入层。该层会把 bracketed-paste 的边界标记吞掉，只把正文转换为普通 `ConsoleKeyInfo` 事件。此时即使终端支持协议，应用也看不到 `ESC[200~` 与 `ESC[201~`。

旧版本曾按“同一批输入同时出现换行与正文”猜测粘贴。这个策略会受终端缓冲方式、快速输入与 IME 上屏影响，不能证明输入确实来自剪贴板，因此已移除。

当前降级规则只有一条：**没有完整协议边界，就作为普通文本输入，不生成粘贴块。**

这意味着某些 Windows Console 主机中的粘贴会直接显示原文；但手动输入、快速输入和 IME 上屏永远保持可逐字符编辑，也不会被错误折叠为原子块。功能降级优先于错误识别。

`IPromptInputSource` 为未来的输入适配器预留了边界：若某个宿主可以可靠读取原始 VT 字节或提供原生粘贴事件，它只需产生 `PromptPasteInputEvent`，无需修改缓冲区、重绘或 Chat 业务代码。

### 5.3 输入源实现与选择

Windows 首选 `WindowsVtPromptInputSource`。它同时启用 VT input、bracketed paste（DEC private mode 2004）和 Win32 Input Mode（private mode 9001）：外层 `ESC[200~ ... ESC[201~` 提供确定的粘贴边界，Win32 key record 则保留 Shift/Ctrl/Alt 修饰状态。

需要特别注意，Win32 Input Mode 开启后，终端可能在 bracketed-paste 正文内部继续使用 `CSI Vk;Sc;Uc;Kd;Cs;Rc_` 表示控制字符。例如换行可能表示为 KeyDown `ESC[13;28;13;1;0;1_` 和 KeyUp `ESC[13;28;13;0;0;1_`。粘贴解析器必须继续解码这层记录：只追加 KeyDown 的 `UnicodeChar`，忽略 KeyUp，再统一换行；否则控制序列会进入用户正文，且原有换行会消失。未知或格式不合法的 CSI 序列必须原样保留，不能静默吞掉。

编辑器不直接调用 `Console.ReadKeyAsync`，而是通过 `IPromptInputSource` 接收事件：

| 输入源 | 适用场景 | 行为 |
| --- | --- | --- |
| `WindowsVtPromptInputSource` | 支持 VT input 的 Windows 终端 | 启用 VT input 与 Win32 Input Mode，同时解析 bracketed paste、Win32 key record 和常用光标键；这是 Windows 首选路径 |
| `WindowsConsolePromptInputSource` | Windows VT input 无法启用时 | 通过 `ReadConsoleInputW` 读取原生键盘记录，保留 Unicode、虚拟键和 Shift/Ctrl/Alt 修饰状态；若宿主不提供粘贴边界则安全显示原文 |
| `VtPromptInputSource` | 任意已提供原始 VT 字符流的宿主 | 跨平台解析器，识别 bracketed paste 与常用光标键；不自行改变终端模式 |
| `ConsolePromptInputSource` | 上述能力不可用时 | 使用 Spectre 的 `ReadKeyAsync`；只在该 API 实际透传边界时折叠，否则显示原文 |

Windows 首先尝试 `ENABLE_VIRTUAL_TERMINAL_INPUT`，将输入转换为 VT 序列供标准输入流读取，再通过 Win32 Input Mode 保留 Shift+Enter 与 Ctrl+Enter 的修饰信息。若设置 VT input mode 失败，工厂才回退到 `ReadConsoleInputW` 原生事件路径。[Microsoft 文档](https://learn.microsoft.com/zh-cn/windows/console/setconsolemode)

## 6. Enter、换行与 CRLF

输入组件区分三类情况：

| 输入 | 处理 |
| --- | --- |
| Enter，输入队列在短暂窗口内保持空闲 | 提交 `buffer.Text` |
| Shift+Enter 或 Ctrl+Enter | 插入普通换行 |
| 后面仍有按键的 Enter | 视为连续文本中的换行，继续收集 |

Windows 或某些终端可能把一次 Enter 表示为 `\r\n` 两个连续事件。为避免把它误认为两次操作，组件在发现连续换行后再次等待：若队列随后空闲，则仍提交；若后面还有正文，则归一为一个 `\n`。

这是当前实现中较容易误读的一段逻辑。它解决的不是文本编码，而是不同终端对“同一物理按键”如何排队输入事件的差异。

## 7. 从编辑态到发送态

用户按 Enter 后，流程不是直接返回字符串，而是分两步：

```text
编辑态
  DisplayText = "请分析：[pasted 1,368 characters]"
          │ Enter
          ▼
提交前揭示（RevealPastedContent）
  清除编辑区摘要并写出 Text 的完整原文
          │
          ▼
ChatApplication
  userInput = PromptInputBuffer.Text
          │
          ▼
Agent.RunStreamAsync / 会话持久化 / 标题生成
  只接收原始文本
```

`RevealPastedContent` 只影响终端显示：它回到锚点、清除摘要占用的物理行、再次回到锚点，然后输出 `buffer.Text`。这样用户翻看终端历史时能看到实际发送的内容，而不是无法复现的摘要。

随后 `ChatApplication` 已经从 `PromptUser()` 获得原始 `userInput`，并将该值用于：

- 命令识别（`/clear`、`/model` 等）；
- 首轮标题生成；
- Agent `RunStreamAsync` 调用；
- Agent 的消息持久化。

因此摘要仅在输入尚未提交时存在，不能成为 Agent 上下文的一部分。

## 8. 终端降级策略

不是所有运行环境都应启动高级多行编辑器。

| 场景 | 策略 | 原因 |
| --- | --- | --- |
| Windows Terminal / 现代 ConHost / 常见 Linux 终端 | 多行编辑器 + 尝试 bracketed paste | 通常支持 VT 输出和逐键输入 |
| Git Bash（mintty 经 winpty） | Spectre 单行 `TextPrompt` | 逐键读取会将 UTF-8 中文拆成字节，且 Win32 坐标 API 不可靠 |
| 输入或输出已重定向 | 不应尝试 bracketed paste | 不存在可交互终端，控制序列可能污染管道输出 |
| `TERM=dumb` | 不应尝试 bracketed paste | 环境明确声明不支持高级终端能力 |

目前 Git Bash 的降级仍是单行输入，因此它不具备本文的折叠粘贴体验。这是刻意的可靠性选择，不应为了功能统一而重新引入中文乱码或输入崩溃。

## 9. 维护约束与常见错误

### 不要把 `DisplayText` 当成用户消息

凡是要发送、持久化、计数 prompt token、生成标题或记录审计日志的地方，必须使用 `PromptInputBuffer.Text`。`DisplayText` 只能进入 `Redraw` 与光标定位。

### 不要把粘贴内容拆回普通字符

若将粘贴内容逐字符插入，删除和光标移动会退化为数千次操作，折叠摘要也失去意义。新增编辑操作时，应首先判断它对粘贴块是否应保持原子性。

### 不要从按键时序推断粘贴

`ConsoleKeyInfo` 不携带“来自剪贴板”的语义。不要以字符数、到达速度、输入批次或换行混合情况创建 `PromptPasteInputEvent`；只有终端协议或原生 API 的明确边界可以产生它。

### 不要忘记在 finally 中关闭 bracketed paste

启用 `ESC[?2004h` 后若不发送 `ESC[?2004l`，同一终端后续运行的程序可能继续收到包装标记。关闭操作必须在正常提交、取消、异常和 EOF 的所有路径执行。

### 不要改回 `\x1b7`

`\x` 是变长十六进制转义；带紧随数字的控制序列必须使用固定四位的 `\u001B`。这是编译期字符串解析问题，和终端兼容性无关。

## 10. 已知限制与后续方向

1. **部分宿主会吞掉协议边界。** Windows Console 会优先尝试 `WindowsVtPromptInputSource`；若无法启用该模式则安全回退。POSIX/SSH 的原始模式适配器尚未实现，但可直接复用 `VtPromptInputSource`，不会影响 Chat 层。
2. **摘要计数目前是 .NET `string.Length`。** 它统计 UTF-16 code unit；emoji 代理对会计为 2。若需要面向用户的 Unicode 标量或字素簇计数，可改为 `Rune` 或 `StringInfo`，并明确产品文案含义。
3. **显示宽度是简化估算。** 组合字符、ZWJ emoji 序列、部分 East Asian Ambiguous Width 字符的列宽仍可能与具体终端不同。需要高保真时，应引入经过验证的 `wcwidth` 实现。
4. **粘贴块当前不可展开编辑。** 用户只能整体删除后重新粘贴，或在块前后追加文本。这是当前交互模型的有意取舍。若未来需要“展开后编辑”，应增加显式命令或按键，不应在普通 Backspace 中隐式展开。
5. **Git Bash 仍降级为单行。** 若要支持它，需要先解决 winpty 下 UTF-8 按键字节拆分问题，并在真实 mintty 环境测试。
6. **交互测试不可完全由单元测试替代。** `PromptInputBuffer` 适合单元测试；真实 bracketed paste、CRLF 事件顺序和终端宽度需要在 Windows Terminal、ConHost、Linux xterm/SSH 与 Git Bash 中做手工冒烟验证。

## 11. 建议的验证清单

每次修改输入逻辑后，至少验证：

1. 普通英文、中文输入后按 Enter 能发送。
2. Shift+Enter / Ctrl+Enter 能插入换行，随后 Enter 能发送。
3. 在支持并透传 bracketed-paste 边界的终端中，粘贴多行文本时显示一个摘要块。
4. 发送后摘要被完整原文替换；Agent 收到的也是完整原文。
5. 在摘要块前后按 Left/Right，光标一次跨块。
6. 在块前按 Delete、在块后按 Backspace，均整块删除且发送文本不含原文。
7. 在无法透传边界的终端中，粘贴显示原文；快速输入和 IME 上屏不会变成 `[pasted N characters]`。
8. Ctrl+C、取消、输入流 EOF 后终端仍可正常使用，后续程序不收到残留的 bracketed paste 标记。
9. 窄终端、含 CJK 与 emoji 的输入不会让光标明显错行。
10. Git Bash、重定向与 `TERM=dumb` 能走降级路径，不输出可见控制字符。

## 12. 一句话模型

把多行输入理解为一个小型终端编辑器：它维护**真实文档**和**编辑态投影**两份表示；粘贴块在投影中压缩，在真实文档中完整保存；提交是从投影回到真实文档的边界。
