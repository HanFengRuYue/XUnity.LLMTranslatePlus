using System;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;
using XUnity_LLMTranslatePlus.Services;

namespace XUnity_LLMTranslatePlus.Views
{
    public sealed partial class AboutPage : Page
    {
        private readonly ConfigService? _configService;
        private readonly LogService? _logService;

        public AboutPage()
        {
            this.InitializeComponent();

            _configService = App.GetService<ConfigService>();
            _logService = App.GetService<LogService>();

            LoadSystemInfo();
        }

        private void LoadSystemInfo()
        {
            if (_configService != null)
            {
                var configPath = _configService.GetConfigFilePath();
                var appDataPath = _configService.GetAppDataFolder();

                ConfigPathText.Text = configPath;
                AppDataPathText.Text = appDataPath;
            }
        }

        private async void OpenAppDataFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_configService != null)
            {
                try
                {
                    var appDataPath = _configService.GetAppDataFolder();
                    await Launcher.LaunchFolderPathAsync(appDataPath);
                }
                catch (Exception ex)
                {
                    _logService?.Log($"打开文件夹失败: {ex.Message}", LogLevel.Error);
                }
            }
        }

        private async void ResetSettings_Click(object sender, RoutedEventArgs e)
        {
            // 显示确认对话框
            ContentDialog dialog = new ContentDialog
            {
                Title = "确认重置",
                Content = "确定要删除配置文件并重置所有设置吗？\n\n这将删除以下内容：\n• API 配置\n• 术语库\n• 翻译设置\n• 所有自定义配置\n\n此操作无法撤销！",
                PrimaryButtonText = "删除并重置",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = this.XamlRoot
            };

            var result = await dialog.ShowAsync();

            if (result == ContentDialogResult.Primary)
            {
                try
                {
                    if (_configService != null)
                    {
                        var configPath = _configService.GetConfigFilePath();
                        var appDataFolder = _configService.GetAppDataFolder();

                        // 删除配置文件
                        if (File.Exists(configPath))
                        {
                            File.Delete(configPath);
                            _logService?.Log("配置文件已删除", LogLevel.Info);
                        }

                        // 删除术语库文件夹
                        var terminologiesFolder = Path.Combine(appDataFolder, "Terminologies");
                        if (Directory.Exists(terminologiesFolder))
                        {
                            Directory.Delete(terminologiesFolder, true);
                            _logService?.Log("术语库文件夹已删除", LogLevel.Info);
                        }

                        // 删除默认术语库文件（如果存在）
                        var defaultTermsFile = Path.Combine(appDataFolder, "terms.csv");
                        if (File.Exists(defaultTermsFile))
                        {
                            File.Delete(defaultTermsFile);
                            _logService?.Log("默认术语库文件已删除", LogLevel.Info);
                        }

                        // 显示成功消息
                        ContentDialog successDialog = new ContentDialog
                        {
                            Title = "重置成功",
                            Content = "配置文件已删除。\n\n请重启应用程序以使更改生效。",
                            CloseButtonText = "确定",
                            XamlRoot = this.XamlRoot
                        };

                        await successDialog.ShowAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logService?.Log($"删除配置文件失败: {ex.Message}", LogLevel.Error);

                    ContentDialog errorDialog = new ContentDialog
                    {
                        Title = "删除失败",
                        Content = $"删除配置文件时出错：{ex.Message}",
                        CloseButtonText = "确定",
                        XamlRoot = this.XamlRoot
                    };

                    await errorDialog.ShowAsync();
                }
            }
        }
    }
}
