# XUnity 大语言模型翻译 Plus

<div align="center">

基于 AI 大语言模型的游戏文本实时翻译工具，为 XUnity.AutoTranslator 提供强大的翻译引擎支持

[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)](https://dotnet.microsoft.com/)
[![WinUI 3](https://img.shields.io/badge/WinUI-3.0-0078D4)](https://github.com/microsoft/microsoft-ui-xaml)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Version](https://img.shields.io/badge/version-2.0.0-green.svg)](https://github.com/your-repo/releases)

</div>

## 最近更新

### v1.2.0 (2025-10-29)

- **Unity 资源提取功能**：从游戏资源文件中提取可翻译文本
  - 支持 Mono/.NET 和 IL2CPP 后端
  - 智能字段扫描和技术数据过滤
  - 右键菜单和智能正则生成
- **UI 改进**：
  - DataGrid 替换 ListView，支持列调整和排序
  - 任务栏进度集成（扫描/提取/翻译操作）
  - 主题切换标准化为 NavigationViewItem
- **术语功能增强**：添加实时搜索和过滤功能
- **性能优化**：相对路径显示，表格高度增加至 600px

## 功能特性

### 核心翻译功能

- **AI 智能翻译**：支持多种大语言模型 API（OpenAI、Claude、Gemini 等兼容接口）
- **实时文件监控**：自动检测游戏翻译文件变化，即时翻译新增文本
- **高性能并发**：可配置并发数（1-100），智能批量写入，避免文件锁冲突
- **上下文感知**：翻译时携带历史上下文（可配置 1-100 条），提升翻译连贯性
- **智能术语管理**：多术语库支持，可创建/切换/删除术语库
- **特殊字符处理**：智能识别并保护转义字符（`\n`、`\r`、`\t`）和特殊符号

### Unity 资源提取功能

- **智能资源扫描**：从 Unity 游戏资源文件中提取可翻译文本
  - 支持 **Mono/.NET** 和 **IL2CPP** 后端
  - 自动检测 Bundle 文件和资源包
  - 无文件大小限制，支持大型关卡文件（level0、level1 等）
- **MonoBehaviour 字段扫描**：
  - **指定字段模式**：扫描配置的字段（如 m_Text、m_Name）
  - **全字段智能模式**：递归扫描所有字符串字段，自动过滤技术数据
    - 8 种启发式规则过滤 GUID、路径、URL、十六进制、Base64 等
    - 可配置递归深度（1-5 层，默认 3 层）
    - 字段黑名单排除（guid、id、path、url 等）
- **高级功能**：
  - 右键菜单：复制文本/路径/文件名、添加字段到黑名单
  - 智能正则生成：自动识别 JSON、坐标、UUID 等模式
  - 排除模式管理：自定义正则表达式过滤不需要翻译的文本
  - DataGrid 可调整列宽和排序
- **导出集成**：一键导出到 XUnity 翻译文件格式

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
[从 Microsoft 官网下载安装包](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads)
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

### 4. 修改 XUnity 配置

1. 找到游戏目录下的 XUnity 配置文件：`BepInEx\config\AutoTranslatorConfig.ini`
2. 打开配置文件，修改以下设置：
   - 将 `Endpoint` 的值改为 `Passthrough`
   - 将 `ReloadTranslationsOnFileChange` 改为 `True`
3. 保存配置文件


### 5. 开始翻译

1. 进入 **主页**
2. 点击 **启动监控** 按钮
3. 启动游戏，应用将自动翻译新出现的文本
4. 实时查看翻译进度和最近翻译
   - 总翻译数、待翻译数、翻译中数、失败数
   - 最近 8 条翻译记录
5. 在 **日志监控** 页面查看详细日志

### 6. Unity 资源提取（可选）

如果游戏文本在资源文件中，可以使用资源提取功能：

1. 进入 **资源提取** 页面
2. 选择游戏数据目录（通常包含以下文件之一）：
   - **Mono/.NET**: `Managed` 文件夹（包含 .dll 文件）
   - **IL2CPP**: `global-metadata.dat` 和 `GameAssembly.dll`
3. 配置扫描选项：
   - **扫描模式**：
     - **指定字段模式**：仅扫描配置的字段（快速，适合已知字段名）
     - **全字段智能模式**：递归扫描所有字符串，自动过滤技术数据（全面）
   - **递归深度**（全字段模式）：1-5 层，默认 3 层
   - **MonoBehaviour 字段**：添加要扫描的字段名（如 `m_Text`、`m_Name`）
4. 点击 **扫描资源文件** 开始提取
5. 在结果表格中：
   - 查看提取的文本和来源路径
   - 右键菜单：复制、添加到黑名单、生成智能正则
   - 调整列宽和排序
6. 配置排除模式（可选）：
   - 添加正则表达式过滤不需要翻译的文本
   - 使用智能正则生成功能快速创建模式
7. 点击 **导出到翻译文件** 保存为 XUnity 格式

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

# 发布 (推荐使用脚本 - 自动构建所有架构并打包)
.\Build-Release.ps1

# 可选参数
.\Build-Release.ps1 -SkipClean  # 跳过清理，加快增量构建

# 手动发布单个架构
dotnet publish --configuration Release --runtime win-x64 --self-contained false /p:PublishSingleFile=true
```

### 发布脚本特性（Build-Release.ps1）

- **多架构构建**：同时构建 x86、x64、ARM64 三个版本
- **自动打包**：使用 7-Zip 最大压缩率生成分发 ZIP 文件
- **并行构建**：提升构建速度
- **输出结构**：
  ```
  Release/
  ├── win-x86/
  │   └── XUnity-LLMTranslatePlus.exe
  ├── win-x64/
  │   └── XUnity-LLMTranslatePlus.exe
  ├── win-arm64/
  │   └── XUnity-LLMTranslatePlus.exe
  ├── XUnity-LLMTranslatePlus-win-x86.zip
  ├── XUnity-LLMTranslatePlus-win-x64.zip
  └── XUnity-LLMTranslatePlus-win-arm64.zip
  ```

### 构建产物

- **EXE 大小**: ~40MB（单文件，Framework-Dependent）
- **ZIP 大小**: ~10MB（75% 压缩率）
- **需求**: 7-Zip 安装在 `C:\Program Files\7-Zip\7z.exe`


## 技术栈

| 技术 | 用途 |
|------| ------|
| .NET | 应用框架 |
| WinUI 3 | UI 框架 |
| CommunityToolkit.WinUI.UI.Controls | DataGrid 组件 |
| AssetsTools.NET | Unity 资源解析 |
| CsvHelper | CSV 处理 |
| HttpClient | HTTP 客户端 |
| DPAPI | 密钥加密 |

## 项目结构

```
XUnity-LLMTranslatePlus/
├── Services/              # 核心服务
│   ├── TranslationService.cs          # 翻译引擎
│   ├── FileMonitorService.cs          # 文件监控
│   ├── TerminologyService.cs          # 术语管理
│   ├── SmartTerminologyService.cs     # AI 术语提取
│   ├── AssetExtractionService.cs      # Unity 资源提取
│   ├── ConfigService.cs               # 配置管理
│   └── LogService.cs                  # 日志服务
├── Views/                 # UI 页面
│   ├── HomePage.xaml                  # 主页
│   ├── ApiConfigPage.xaml             # API 配置
│   ├── TranslationSettingsPage.xaml   # 翻译设置
│   ├── TerminologyPage.xaml           # 术语管理
│   ├── AssetExtractionPage.xaml       # 资源提取
│   └── LogPage.xaml                   # 日志查看
├── Models/                # 数据模型
│   ├── AppConfig.cs                   # 应用配置
│   ├── ExtractedText.cs               # 提取的文本数据
│   └── ...
└── Utils/                 # 工具类
    ├── PathValidator.cs               # 路径验证
    ├── SpecialCharProcessor.cs        # 特殊字符处理
    └── ...
```

## 贡献

欢迎提交 Issue 和 Pull Request！

## 许可证

MIT License - 详见 [LICENSE](LICENSE) 文件

## 相关项目

- [XUnity.AutoTranslator](https://github.com/bbepis/XUnity.AutoTranslator) - Unity 游戏自动翻译框架

## 问题反馈

如遇到问题，请提供以下信息：
1. 操作系统版本和架构（x86/x64/ARM64）
2. 错误截图或日志（在 **日志监控** 页面）
3. 复现步骤
4. 如使用资源提取功能，请说明：
   - 游戏使用的 Unity 后端（Mono/IL2CPP）
   - 扫描模式和配置参数
   - 目标文件/文件夹路径

## 致谢

感谢所有为本项目做出贡献的开发者和用户！
