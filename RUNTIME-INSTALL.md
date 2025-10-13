# XUnity-LLMTranslatePlus 运行时安装指南

## 概述

从该版本开始，XUnity-LLMTranslatePlus 采用**框架依赖部署模式**，大幅减小了程序体积（从84MB降至约20-30MB）。

使用此程序前，您需要在计算机上安装以下运行时环境：

---

## 必需的运行时环境

### 1. .NET 9 Desktop Runtime

#### 下载地址
🔗 **官方下载页面**: https://dotnet.microsoft.com/download/dotnet/9.0

#### 安装步骤
1. 访问上述链接
2. 在 "Run desktop apps" 部分，根据您的系统架构选择：
   - **x64** - 适用于大多数现代64位Windows系统
   - **x86** - 适用于32位Windows系统（较少见）
   - **Arm64** - 适用于ARM架构Windows设备
3. 下载后运行安装程序，按照向导完成安装

#### 验证安装
打开命令提示符（CMD）或PowerShell，输入以下命令：
```bash
dotnet --list-runtimes
```

如果看到类似以下输出，说明安装成功：
```
Microsoft.WindowsDesktop.App 9.0.x [C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App]
```

---

### 2. Windows App SDK 1.8 Runtime

#### 自动安装（推荐）
Windows App SDK Runtime 通常会在您首次运行程序时**自动从Microsoft Store下载安装**。

如果程序启动时提示缺少运行时，Windows会自动引导您安装。

#### 手动安装（可选）
如果自动安装失败，可以手动下载安装：

1. **方法A：通过Microsoft Store**
   - 打开Microsoft Store
   - 搜索 "Windows App Runtime"
   - 点击安装

2. **方法B：离线安装包**
   - 访问：https://github.com/microsoft/WindowsAppSDK/releases
   - 下载最新的 `Microsoft.WindowsAppSDK.1.8.x` 安装包
   - 根据系统架构选择对应的安装包（x64/x86/ARM64）

---

## 系统要求

| 项目 | 最低要求 | 推荐配置 |
|------|---------|---------|
| 操作系统 | Windows 10 1809 (Build 17763) | Windows 10 22H2 或 Windows 11 |
| .NET Runtime | .NET 9.0 Desktop Runtime | 最新版本 |
| Windows App SDK | 1.8.0 或更高 | 1.8.x 最新版 |
| 系统架构 | x64, x86, ARM64 | x64 |

---

## 常见问题

### Q: 为什么改为框架依赖部署？
**A:** 框架依赖部署将程序体积从84MB减小至约20-30MB，降低了下载和存储成本。运行时环境只需安装一次，可供多个.NET应用共享。

### Q: 程序无法启动，提示缺少运行时
**A:** 请按照上述步骤安装.NET 9 Desktop Runtime和Windows App SDK Runtime。

### Q: 如何确认我安装的运行时版本？
**A:**
- **.NET Runtime**: 在命令行输入 `dotnet --list-runtimes`
- **Windows App SDK**: 检查 `C:\Program Files\WindowsApps\` 目录下是否有 `Microsoft.WindowsAppRuntime` 开头的文件夹

### Q: 我可以在多台电脑上使用同一个程序文件吗？
**A:** 可以！只要目标电脑上安装了所需的运行时环境，程序文件可以在多台电脑间复制使用。

### Q: 卸载程序时需要卸载运行时吗？
**A:** 不需要。运行时环境可以被其他.NET应用共享使用，建议保留在系统中。

---

## 企业部署建议

对于企业批量部署，建议：

1. **预安装运行时**：在基础系统镜像中预装.NET 9 Desktop Runtime和Windows App SDK
2. **使用离线安装包**：准备运行时的离线安装包，便于内网环境部署
3. **Group Policy部署**：通过组策略统一推送运行时安装

---

## 技术支持

如遇到运行时安装问题，请：

1. 查看项目 GitHub Issues: https://github.com/YOUR_REPO/issues
2. 参考微软官方文档：
   - .NET 9 文档：https://learn.microsoft.com/dotnet/core/whats-new/dotnet-9
   - Windows App SDK 文档：https://learn.microsoft.com/windows/apps/windows-app-sdk/

---

**最后更新日期**: 2025年10月

**适用版本**: XUnity-LLMTranslatePlus v1.0.4+
