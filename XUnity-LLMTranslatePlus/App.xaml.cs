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
            InitializeComponent();
            ConfigureServices();
        }

        /// <summary>
        /// 配置依赖注入服务
        /// </summary>
        private void ConfigureServices()
        {
            var services = new ServiceCollection();

            // 注册所有服务为单例
            services.AddSingleton<LogService>();
            services.AddSingleton<ConfigService>();
            services.AddSingleton<TerminologyService>();
            services.AddSingleton<ApiClient>();
            services.AddSingleton<TranslationService>();
            services.AddSingleton<FileMonitorService>();
            services.AddSingleton<TextEditorService>();

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
