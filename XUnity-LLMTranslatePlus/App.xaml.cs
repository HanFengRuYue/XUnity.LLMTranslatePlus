using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using XUnity_LLMTranslatePlus.Services;

namespace XUnity_LLMTranslatePlus
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        public static Window? MainWindow { get; private set; }
        private static ServiceProvider? _serviceProvider;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            // Required for PublishSingleFile with framework-dependent deployment
            // This allows Windows App SDK to locate runtime components extracted from the single file
            Environment.SetEnvironmentVariable("MICROSOFT_WINDOWSAPPRUNTIME_BASE_DIRECTORY", AppContext.BaseDirectory);

            InitializeComponent();
            ConfigureServices();
        }

        /// <summary>
        /// 配置依赖注入服务
        /// </summary>
        private void ConfigureServices()
        {
            var services = new ServiceCollection();

            // 注册 HttpClient 工厂（.NET 最佳实践）
            services.AddHttpClient();

            // 注册所有服务为单例（注意顺序：依赖关系）
            services.AddSingleton<LogService>();
            services.AddSingleton<ConfigService>();
            services.AddSingleton<TerminologyService>();
            services.AddSingleton<ApiClient>();
            services.AddSingleton<ApiPoolManager>();           // 新增：API池管理器
            services.AddSingleton<TranslationDispatcher>();    // 新增：翻译分发器
            services.AddSingleton<SmartTerminologyService>();
            services.AddSingleton<TranslationService>();
            services.AddSingleton<FileMonitorService>();
            services.AddSingleton<TextEditorService>();
            services.AddSingleton<HotkeyService>();
            services.AddSingleton<AssetScannerService>();   // 资产扫描服务
            services.AddSingleton<PreTranslationService>(); // 预翻译服务
            services.AddSingleton<TaskbarProgressService>(); // 任务栏进度服务

            _serviceProvider = services.BuildServiceProvider();

            // 初始化日志服务
            var logService = GetService<LogService>();
            logService?.Log("应用程序启动", LogLevel.Info);
        }

        /// <summary>
        /// 获取服务实例
        /// </summary>
        public static T? GetService<T>() where T : class
        {
            return _serviceProvider?.GetService<T>();
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            MainWindow = new MainWindow();
            MainWindow.Activate();
        }
    }
}
