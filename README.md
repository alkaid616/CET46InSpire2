# CET46InSpire2

CET46InSpire2 是基于 [sleepyHolo/SpireMod_CET46InSpire](https://github.com/sleepyHolo/SpireMod_CET46InSpire) 的 Slay the Spire 2 / RitsuLib 复刻移植版。当前版本已复刻原模组的核心玩法，并根据 STS2 的 Godot/C# API 与运行时模型做了必要适配。

特别感谢原项目作者提供的玩法、资源与词库基础；也感谢 [BAKAOLC/STS2-RitsuLib](https://github.com/BAKAOLC/STS2-RitsuLib)，本项目依赖该基础库完成 STS2 内容注册、补丁和数据持久化等工作。

## 功能复刻状态

| 功能 | 状态 | 说明 |
| --- | --- | --- |
| CET/JLPT 系列遗物 | 已复刻 | 事件遗物、词库倾向、图标和说明文本已迁移到 STS2 模型。 |
| CET 呼唤事件 | 已复刻并适配 | 开局 Neow 后注入事件，终局奖励页后也会触发适配版事件。 |
| 打牌前单词测验 | 已复刻 | 手动打牌前弹出测验，诅咒/状态牌不触发。 |
| 分数倍率 | 已复刻并适配 | 当前答题分数影响该牌的伤害、格挡和关键数值效果。 |
| 错题本 | 已复刻 | 0 分题进入本局错题本，战斗中右键遗物可回顾。 |
| 纠错奖励药水 | 已复刻 | 每场战斗第一次完美纠错可获得随机药水；无错题时也会直接尝试发放。 |
| 连对 Power | 已复刻 | 连续满分计数达到阈值后按原规则施加 Weak/Vulnerable。 |
| 配置面板 | 已复刻并适配 | 支持深色、纯字体、快速、休闲、自动返回、显示词库、题目数量和词库权重等配置。 |
| 词库 | 已复刻 | CET4、CET6、N1、N2、N3、N4 词库与原项目文件哈希一致。 |
| 本地化 | 已复刻并适配 | 支持简体中文和英文；繁中环境回退使用简中逻辑。 |
| 调试命令 | 已适配 | 提供 STS2 控制台 `quiz` 命令用于开题或发放测试遗物。 |

## STS2 适配差异

- 原项目是 STS1 Java / BaseMod / ModTheSpire 模组；本项目是 STS2 Godot/C# 模组，并通过 RitsuLib 注册内容和补丁。
- 原项目 README 中提到的 Downfall 兼容属于 STS1 环境，本项目不复刻该兼容层；STS2 侧改为对当前游戏流程中的 Neow 后事件和终局奖励页后事件做适配。
- 原项目资源里存在 N5 图标和旧描述残留，但实际可用词库为 CET4、CET6、N1、N2、N3、N4；本项目沿用这些实际词库范围。
- CET 日期提示使用当前移植版内置预测表，而不是原项目旧版的固定 2025 日期。
- 答题界面由 STS1 的 BaseMod CustomScreen 改为 STS2 Godot Overlay，交互体验会与原版实现存在平台层差异。

## 词库与资源核对

当前移植版词库与原项目对应文件一致：

| 词库 | 词条数量 |
| --- | ---: |
| CET4 | 4028 |
| CET6 | 1652 |
| N1 | 4085 |
| N2 | 2911 |
| N3 | 1579 |
| N4 | 1568 |

UI 与遗物主要图片资源也已从原项目迁移。STS2 侧 Power 图标使用当前模型资源，不再保留 STS1 版本的 32/84 双尺寸结构。

## 构建与安装

1. 复制 `local.props.template` 为 `local.props`。
2. 在 `local.props` 中配置：
   - `Sts2Dir`：Slay the Spire 2 安装目录。
   - `Sts2DataDir`：游戏数据目录，默认通常是 `$(Sts2Dir)\data_sts2_windows_x86_64`。
   - `GodotExe`：MegaDot/Godot C# 可执行文件路径，用于导出 PCK。
3. 执行构建：

```powershell
dotnet build CET46InSpire2.csproj
```

构建目标会把 DLL、manifest 和 PCK 复制到 `$(Sts2Dir)\mods\CET46InSpire2`。如果只验证 C# 编译、不导出 PCK，可以运行：

```powershell
dotnet build CET46InSpire2.csproj -p:RunPckExport=false
```

## GitHub Release 自动发布

仓库包含 GitHub Actions 发布流程：`.github/workflows/release.yml`。推送 `v*` 标签或手动运行 `GitHub Release` workflow 后，会构建 Release 包、上传构建产物，并创建或更新同名 GitHub Release。

发布前需要在 GitHub 仓库配置以下任一依赖来源：

- 自托管 Windows runner：配置仓库变量 `RELEASE_RUNNER`、`STS2_DIR`、`GODOT_EXE`，可选配置 `STS2_DATA_DIR`。
- GitHub 托管 runner：配置 secret `STS2_RELEASE_DEPS_URL` 指向私有依赖压缩包，可选配置 `STS2_RELEASE_DEPS_TOKEN`。压缩包中需要包含带有 `sts2.dll` 和 `0Harmony.dll` 的游戏数据目录，以及 MegaDot/Godot 可执行文件。

发版步骤：

```powershell
# 1. 更新 CET46InSpire2.json 中的 version，例如 1.0.1
# 2. 提交版本变更
git tag v1.0.1
git push origin v1.0.1
```

Release 资产命名为 `CET46InSpire2-<version>.zip`。workflow 会校验 tag 必须等于 manifest 版本，例如 `version` 为 `1.0.1` 时 tag 必须是 `v1.0.1`。

## 词库来源

词库来源说明继承自原项目：

- CET4: [大学英语四级单词(词典重制完美版)](https://ankiweb.net/shared/info/1378032490)
- CET6: [大学六级英语单词全集（修订版）](https://ankiweb.net/shared/info/2125686844)
- JLPT: 感谢 5mdld 授权，词库仓库为 [5mdld/anki-jlpt-decks](https://github.com/5mdld/anki-jlpt-decks)

如词库来源存在侵权问题，请联系维护者处理。

## License

本项目作为 [sleepyHolo/SpireMod_CET46InSpire](https://github.com/sleepyHolo/SpireMod_CET46InSpire) 的复刻移植版，沿用 GNU General Public License v3.0，详见 [LICENSE](LICENSE)。

[STS2-RitsuLib](https://github.com/BAKAOLC/STS2-RitsuLib) 按其原仓库的 MIT License 授权；本项目仅依赖并感谢该基础库。
