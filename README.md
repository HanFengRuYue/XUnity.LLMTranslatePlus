# XUnity 大语言模型翻译 Plus

<div align="center">

基于 AI 大语言模型的游戏文本实时翻译工具，为 XUnity.AutoTranslator 提供强大的翻译引擎支持

[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/)
[![WinUI 3](https://img.shields.io/badge/WinUI-3.0-0078D4)](https://github.com/microsoft/microsoft-ui-xaml)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

</div>

## 功能特性

- **AI 智能翻译**：支持多种大语言模型 API（OpenAI、Claude、Gemini 等兼容接口）
- **实时文件监控**：自动检测游戏翻译文件变化，即时翻译新增文本
- **高性能并发**：可配置并发数（1-100），智能批量写入，避免文件锁冲突
- **上下文感知**：翻译时携带历史上下文（可配置 1-100 条），提升翻译连贯性
- **智能术语管理**：
  - AI 自动提取专有名词（人名、地名、技能名等）
  - 多术语库支持，按优先级和长度智能应用
  - 术语 CSV 导入/导出
- **特殊字符处理**：智能识别并保护转义字符（`\n`、`\r`、`\t`）和特殊符号
- **可视化界面**：WinUI 3 现代化界面，实时查看翻译进度和日志
- **安全性**：API 密钥使用 Windows DPAPI 加密存储

## 系统要求

- **操作系统**：Windows 10 1809 (Build 17763) 或更高版本
- **运行时**：
  - [.NET 9 Desktop Runtime (x64)](https://dotnet.microsoft.com/download/dotnet/9.0)
  - [Windows App SDK 1.8 Runtime](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)

## 快速开始

### 1. 安装运行时

首次使用需要安装以下运行时（仅需安装一次）：

```powershell
# 安装 .NET 9 Desktop Runtime
winget install Microsoft.DotNet.DesktopRuntime.9

# 安装 Windows App SDK 1.8 Runtime
# 从 Microsoft 官网下载安装包
```

### 2. 配置 API

1. 打开应用，进入 **API 配置** 页面
2. 填写 API 端点和密钥（支持 OpenAI 兼容接口）
3. 点击 **测试连接** 验证配置
4. 设置完成后会自动保存（800ms 防抖）

### 3. 配置翻译设置

1. 进入 **翻译设置** 页面
2. 选择 XUnity.AutoTranslator 翻译文件目录
   - 通常位于：`游戏目录\BepInEx\Translation\zh\Text\`
3. 设置目标语言（默认：简体中文）
4. 调整并发数和上下文行数（可选）

### 4. 开始翻译

1. 进入 **主页**
2. 点击 **启动监控** 按钮
3. 启动游戏，应用将自动翻译新出现的文本
4. 实时查看翻译进度和最近翻译

## 构建说明

### 开发环境

- Visual Studio 2022 (17.12+) 或 Visual Studio Code
- .NET 9.0 SDK
- Windows App SDK 1.8

### 构建命令

```bash
# 开发构建
dotnet build

# 运行
dotnet run --project XUnity-LLMTranslatePlus/XUnity-LLMTranslatePlus.csproj

# 发布 (推荐使用脚本)
.\Build-Release.ps1

# 手动发布
dotnet publish --configuration Release --runtime win-x64 --self-contained false /p:PublishSingleFile=true
```

构建产物：
- **Debug**: `~40MB` 可执行文件
- **Release**: `~40MB` 单文件可执行文件（无 PDB）

## 架构亮点

- **异步锁机制**：使用 `SemaphoreSlim` 替代传统 `lock`，避免死锁
- **Channel<T> 批处理**：基于 Channel 的高性能日志和文件写入批处理
- **线程安全上下文缓存**：多线程环境下的安全上下文管理
- **并发文件访问**：使用 `FileShare.ReadWrite` 与 XUnity 共存
- **正则表达式优化**：使用 `[GeneratedRegex]` 提升 3-140 倍性能
- **依赖注入**：基于 `Microsoft.Extensions.DependencyInjection` 的服务管理

## 技术栈

| 技术 | 版本 | 用途 |
|------|------|------|
| .NET | 9.0 | 应用框架 |
| WinUI 3 | 1.8 | UI 框架 |
| CsvHelper | 33.1.0 | CSV 处理 |
| HttpClient | - | HTTP 客户端 |
| DPAPI | - | 密钥加密 |

## 项目结构

```
XUnity-LLMTranslatePlus/
├── Services/              # 核心服务
│   ├── TranslationService.cs          # 翻译引擎
│   ├── FileMonitorService.cs          # 文件监控
│   ├── TerminologyService.cs          # 术语管理
│   ├── SmartTerminologyService.cs     # AI 术语提取
│   ├── ConfigService.cs               # 配置管理
│   └── LogService.cs                  # 日志服务
├── Views/                 # UI 页面
│   ├── HomePage.xaml                  # 主页
│   ├── ApiConfigPage.xaml             # API 配置
│   ├── TranslationSettingsPage.xaml   # 翻译设置
│   ├── TerminologyPage.xaml           # 术语管理
│   └── LogPage.xaml                   # 日志查看
├── Models/                # 数据模型
└── Utils/                 # 工具类
```

## 贡献

欢迎提交 Issue 和 Pull Request！

## 许可证

MIT License

## 相关项目

- [XUnity.AutoTranslator](https://github.com/bbepis/XUnity.AutoTranslator) - Unity游戏自动翻译框架
