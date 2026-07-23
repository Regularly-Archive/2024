# ConfigCommand 国际化资源清单

> 文件：`src/InsightaAI.Agent.Cli/Commands/ConfigCommand.cs`

## 国际化状态：已完成

所有用户可见字符串均已通过 `CliStrings` 引用，无硬编码字符串。

## 引用的 CliStrings 资源 key

### 命令与选项描述

| Key | 用途 | 位置 |
|-----|------|------|
| ConfigDescription | config 命令描述 | L18 |
| ConfigProviderDescription | provider 子命令描述 | L20 |
| ConfigModelDescription | model 子命令描述 | L23 |
| ConfigLanguageDescription | language 子命令描述 | L26 |

### Provider 管理

| Key | 用途 | 位置 | 格式参数 |
|-----|------|------|---------|
| ConfigProviderManagementTitle | Provider 管理标题 | L108 | 无 |
| ConfigAddProvider | 添加供应商 | L98 | 无 |
| ConfigEditProvider | 编辑供应商 | L102 | 无 |
| ConfigDeleteProvider | 删除供应商 | L103 | 无 |
| ConfigProviderNamePrompt | 供应商名称输入提示 | L132 | 无 |
| ConfigProviderExistsFormat | 供应商已存在警告 | L136 | `{0}` = name |
| ConfigSelectAdapter | 选择适配器提示 | L140 | 无 |
| ConfigAdapterOpenAi | openai 适配器选项 | L439 | 无 |
| ConfigAdapterOpenAiResponse | openai-response 适配器选项 | L440 | 无 |
| ConfigAdapterAnthropic | anthropic 适配器选项 | L441 | 无 |
| ConfigAdapterGemini | gemini 适配器选项 | L442 | 无 |
| ConfigApiKeyPrompt | API Key 输入提示 | L147 | 无 |
| ConfigBaseUrlOptionalPrompt | Base URL 输入提示 | L151 | 无 |
| ConfigProviderAddedFormat | 供应商已添加 | L161 | `{0}` = name |
| ConfigSelectProviderToEdit | 选择要编辑的供应商 | L167 | 无 |
| ConfigAdapterCurrentFormat | 适配器（当前值） | L176 | `{0}` = adapter |
| ConfigApiKeyKeepPrompt | API Key 保持提示 | L183 | 无 |
| ConfigBaseUrlCurrentFormat | Base URL（当前值） | L189 | `{0}` = currentBaseUrl |
| ConfigProviderUpdatedFormat | 供应商已更新 | L200 | `{0}` = name |
| ConfigSelectProviderToDelete | 选择要删除的供应商 | L206 | 无 |
| ConfigDeleteProviderConfirmFormat | 删除供应商确认 | L212 | `{0}` = name |
| ConfigProviderDeletedFormat | 供应商已删除 | L216 | `{0}` = name |

### Model 管理

| Key | 用途 | 位置 | 格式参数 |
|-----|------|------|---------|
| ConfigModelManagementTitle | 模型管理标题 | L241 | 无 |
| ConfigAddModel | 添加模型 | L230 | 无 |
| ConfigEditModel | 编辑模型 | L234 | 无 |
| ConfigDeleteModel | 删除模型 | L235 | 无 |
| ConfigSelectPrimaryModel | 选择主模型 | L237 | 无 |
| ConfigModelReferencePrompt | 模型引用输入提示 | L268 | 无 |
| ConfigModelExistsFormat | 模型已存在警告 | L272 | `{0}` = key |
| ConfigModelIdPrompt | Model ID 输入提示 | L277 | 无 |
| ConfigMaxTokensOptionalPrompt | Max Tokens 输入提示 | L281 | 无 |
| ConfigContextWindowOptionalPrompt | Context Window 输入提示 | L285 | 无 |
| ConfigModelAddedFormat | 模型已添加 | L300 | `{0}` = key |
| ConfigSelectModelToEdit | 选择要编辑的模型 | L306 | 无 |
| ConfigModelIdCurrentFormat | Model ID（当前值） | L315 | `{0}` = modelId |
| ConfigMaxTokensCurrentFormat | Max Tokens（当前值） | L322 | `{0}` = currentMaxTokens |
| ConfigContextWindowCurrentFormat | Context Window（当前值） | L329 | `{0}` = currentContextWindow |
| ConfigModelUpdatedFormat | 模型已更新 | L342 | `{0}` = key |
| ConfigSelectModelToDelete | 选择要删除的模型 | L348 | 无 |
| ConfigDeleteModelConfirmFormat | 删除模型确认 | L354 | `{0}` = key |
| ConfigModelDeletedFormat | 模型已删除 | L358 | `{0}` = key |
| ConfigNoModels | 没有配置任何模型 | L367 | 无 |
| ConfigSelectPrimaryModelCurrentFormat | 选择主模型（当前值） | L372 | `{0}` = currentPrimaryModel |
| ConfigPrimaryModelSetFormat | 主模型已设置 | L382 | `{0}` = model |
| ConfigConfigureSecondaryModelPrompt | 配置副模型提示 | L386 | 无 |
| ConfigSelectSecondaryModel | 选择副模型 | L389 | 无 |
| ConfigSecondaryModelSetFormat | 副模型已设置 | L397 | `{0}` = model |

### Language 管理

| Key | 用途 | 位置 | 格式参数 |
|-----|------|------|---------|
| ConfigLanguagePrompt | 选择语言提示 | L62 | 无 |
| ConfigLanguageAuto | 自动选项 | L64 | 无 |
| ConfigLanguageEnglish | 英文选项 | L65 | 无 |
| ConfigLanguageChinese | 简体中文选项 | L66 | 无 |
| ConfigLanguageSetFormat | 语言已设置 | L83 | `{0}` = language |

### 导航与通用

| Key | 用途 | 位置 |
|-----|------|------|
| ConfigMenuNavigationHint | 菜单导航提示 | L91, L223 |
| ConfigSaved | 配置已保存 | L42, L53 |

### 共享资源

| Key | 用途 | 位置 |
|-----|------|------|
| CommonBack | 返回 | L105, L238 |
| CommonNone | 无 | L187 |
| CommonDefault | 默认 | L320, L327 |
| ErrorPrefix | 错误前缀（通过 ShowError 间接使用） | — |

## 硬编码字符串

无

## 备注

- `PromptMenu` 有两个重载：带 `cancelResult` 的支持 ESC 返回，不带的未使用（遗留代码）
- 一级菜单同时提供"返回"菜单项和 ESC 快捷键
- 二级菜单通过 `PromptSelection`（cancelResult = `"\0"`）和 `SelectAdapter`（cancelResult = `AdapterAction.Cancel`）实现 ESC 返回
