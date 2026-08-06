# ToolPermissionHook 国际化资源清单

> 文件：`src/InsightaAI.Agent.Cli/Hooks/ToolPermissionHook.cs`

## 国际化状态：已完成

工具权限确认流程的所有用户可见字符串均已通过 `CliStrings` 引用，无硬编码字符串。
颜色 markup 标签保留在代码侧拼接，资源值仅含可翻译纯文本。

## 引用的 CliStrings 资源 key

| Key | 用途 | 位置 | 格式参数 | resx 行号 |
|-----|------|------|---------|-----------|
| ToolPermissionWantsToUseFormat | 工具调用提示语 | L38 | `{0}` = toolName | 538 |
| ToolPermissionProceedTitle | 确认标题 | L58 | 无 | 535 |
| ToolPermissionAllow | 允许选项 | L61 | 无 | 541 |
| ToolPermissionAllowAlways | 允许且本次会话不再询问 | L62 | 无 | 544 |
| ToolPermissionReject | 拒绝选项 | L63 | 无 | 547 |

> resx 行号对应 `Resources/CliStrings.resx`（英文），`CliStrings.zh-CN.resx` 结构相同。
> 位置行号对应 `Hooks/ToolPermissionHook.cs`。

## 硬编码字符串

无

## 备注

- 选项匹配使用枚举 `ToolPermissionChoice { Allow, AllowAlways, Reject }`，避免本地化文本参与 switch 匹配。
- 选项通过公共类型 `MenuChoice<TAction>`（`Models/MenuChoice.cs`）承载：`Value` 稳定用于匹配，`Label` 本地化用于展示（`UseConverter(c => c.Label)`）。
- `MenuChoice<T>` 与 ConfigCommand 共用，勿在 Hook 内复制私有实现。
- 第 35 行的 `[yellow]●[/]` 图标与 `[cyan]...[/]` 工具名高亮在代码侧用插值拼接，翻译时无需关心 Spectre markup。
