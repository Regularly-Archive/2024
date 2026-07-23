# SkillsCommand 国际化资源清单

> 文件：`src/InsightaAI.Agent.Cli/Commands/SkillsCommand.cs`

## 国际化状态：已完成

所有用户可见字符串均已通过 `CliStrings` 引用，无硬编码字符串。

## 引用的 CliStrings 资源 key

### 命令与选项描述

| Key | 用途 | 位置 |
|-----|------|------|
| SkillsDescription | skills 命令描述 | L20 |
| SkillsListDescription | list 子命令描述 | L23 |
| SkillsInstallDescription | install 子命令描述 | L29 |
| SkillsUninstallDescription | uninstall 子命令描述 | L37 |
| SkillsPathArgumentDescription | path 参数描述 | L30 |
| SkillsNameArgumentDescription | name 参数描述 | L38 |
| ScopeOptionDescription | --scope 选项描述 | L24, L39 |
| ScopeOptionDescriptionWithDefault | --scope 选项描述（带默认值） | L31 |

### 运行时消息

| Key | 用途 | 位置 | 格式参数 |
|-----|------|------|---------|
| SkillsListEmpty | Skills 列表为空 | L81, L95 | 无 |
| SkillsListFieldName | 表格列：名称 | L100 | 无 |
| SkillsListFieldDescription | 表格列：描述 | L101 | 无 |
| SkillSourceDirectoryNotFoundFormat | 目录不存在 | L122 | `{0}` = sourcePath |
| SkillManifestMissing | SKILL.md 缺失 | L132 | 无 |
| SkillManifestInvalid | SKILL.md 无效 | L142 | 无 |
| SkillOverwritePromptFormat | 覆盖确认提示 | L158 | `{0}` = skillName |
| SkillInstalledFormat | 已安装提示 | L170 | `{0}` = name, `{1}` = targetDir |
| SkillNotFoundFormat | 未找到提示 | L198 | `{0}` = skillName |
| SkillRemovedFormat | 已移除提示 | L227 | `{0}` = scope, `{1}` = name |

### 共享资源

| Key | 用途 | 位置 |
|-----|------|------|
| CommonCancelled | 已取消 | L161 |
| ErrorPrefix | 错误前缀 | L125, L132, L142 |
| ScopeGlobal | 全局 | L239 |
| ScopeProject | 项目 | L238 |

## 硬编码字符串

无
