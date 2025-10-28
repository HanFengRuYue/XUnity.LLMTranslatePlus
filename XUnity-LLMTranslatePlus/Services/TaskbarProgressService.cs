using System;
using System.Runtime.InteropServices;
using XUnity_LLMTranslatePlus.Models;

namespace XUnity_LLMTranslatePlus.Services
{
    /// <summary>
    /// 任务栏进度指示器服务 - 通过Win32 ITaskbarList3 COM接口实现
    /// </summary>
    public class TaskbarProgressService
    {
        private readonly LogService? _logService;
        private ITaskbarList3? _taskbarList;
        private IntPtr _windowHandle = IntPtr.Zero;
        private bool _initialized = false;

        public TaskbarProgressService(LogService? logService = null)
        {
            _logService = logService;
        }

        /// <summary>
        /// 初始化任务栏进度服务
        /// </summary>
        /// <param name="windowHandle">主窗口句柄（HWND）</param>
        public void Initialize(IntPtr windowHandle)
        {
            if (_initialized || windowHandle == IntPtr.Zero)
                return;

            try
            {
                _windowHandle = windowHandle;
                _taskbarList = (ITaskbarList3)new CTaskbarList();
                _taskbarList.HrInit();
                _initialized = true;

                _logService?.LogAsync("任务栏进度服务已初始化", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                _logService?.LogAsync($"任务栏进度服务初始化失败: {ex.Message}", LogLevel.Warning);
                _taskbarList = null;
                _initialized = false;
            }
        }

        /// <summary>
        /// 设置进度值（0.0 - 1.0）
        /// </summary>
        /// <param name="value">进度值，范围 0.0 到 1.0</param>
        public void SetProgressValue(double value)
        {
            if (!_initialized || _taskbarList == null || _windowHandle == IntPtr.Zero)
                return;

            try
            {
                // 将 0.0-1.0 转换为 0-100
                ulong completed = (ulong)(value * 100);
                ulong total = 100;

                _taskbarList.SetProgressValue(_windowHandle, completed, total);
            }
            catch (Exception ex)
            {
                _logService?.LogAsync($"设置任务栏进度值失败: {ex.Message}", LogLevel.Debug);
            }
        }

        /// <summary>
        /// 设置进度状态
        /// </summary>
        /// <param name="state">进度状态</param>
        public void SetProgressState(TaskbarProgressState state)
        {
            if (!_initialized || _taskbarList == null || _windowHandle == IntPtr.Zero)
                return;

            try
            {
                TBPFLAG flag = state switch
                {
                    TaskbarProgressState.Normal => TBPFLAG.TBPF_NORMAL,
                    TaskbarProgressState.Paused => TBPFLAG.TBPF_PAUSED,
                    TaskbarProgressState.Error => TBPFLAG.TBPF_ERROR,
                    TaskbarProgressState.Indeterminate => TBPFLAG.TBPF_INDETERMINATE,
                    TaskbarProgressState.NoProgress => TBPFLAG.TBPF_NOPROGRESS,
                    _ => TBPFLAG.TBPF_NOPROGRESS
                };

                _taskbarList.SetProgressState(_windowHandle, flag);
            }
            catch (Exception ex)
            {
                _logService?.LogAsync($"设置任务栏进度状态失败: {ex.Message}", LogLevel.Debug);
            }
        }

        /// <summary>
        /// 显示正常进度
        /// </summary>
        /// <param name="value">进度值，范围 0.0 到 1.0</param>
        public void ShowProgress(double value)
        {
            SetProgressState(TaskbarProgressState.Normal);
            SetProgressValue(value);
        }

        /// <summary>
        /// 显示不确定进度（滚动条模式）
        /// </summary>
        public void ShowIndeterminate()
        {
            SetProgressState(TaskbarProgressState.Indeterminate);
        }

        /// <summary>
        /// 显示暂停进度（黄色）
        /// </summary>
        /// <param name="value">进度值，范围 0.0 到 1.0</param>
        public void ShowPaused(double value)
        {
            SetProgressState(TaskbarProgressState.Paused);
            SetProgressValue(value);
        }

        /// <summary>
        /// 显示错误进度（红色）
        /// </summary>
        /// <param name="value">进度值，范围 0.0 到 1.0</param>
        public void ShowError(double value)
        {
            SetProgressState(TaskbarProgressState.Error);
            SetProgressValue(value);
        }

        /// <summary>
        /// 隐藏进度
        /// </summary>
        public void HideProgress()
        {
            SetProgressState(TaskbarProgressState.NoProgress);
        }

        #region Win32 Interop

        [ComImport]
        [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
        [ClassInterface(ClassInterfaceType.None)]
        private class CTaskbarList { }

        [ComImport]
        [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ITaskbarList3
        {
            // ITaskbarList
            [PreserveSig]
            void HrInit();

            [PreserveSig]
            void AddTab(IntPtr hwnd);

            [PreserveSig]
            void DeleteTab(IntPtr hwnd);

            [PreserveSig]
            void ActivateTab(IntPtr hwnd);

            [PreserveSig]
            void SetActiveAlt(IntPtr hwnd);

            // ITaskbarList2
            [PreserveSig]
            void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

            // ITaskbarList3
            [PreserveSig]
            void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);

            [PreserveSig]
            void SetProgressState(IntPtr hwnd, TBPFLAG tbpFlags);
        }

        private enum TBPFLAG
        {
            TBPF_NOPROGRESS = 0x0,
            TBPF_INDETERMINATE = 0x1,
            TBPF_NORMAL = 0x2,
            TBPF_ERROR = 0x4,
            TBPF_PAUSED = 0x8
        }

        #endregion
    }

    /// <summary>
    /// 任务栏进度状态
    /// </summary>
    public enum TaskbarProgressState
    {
        /// <summary>
        /// 无进度
        /// </summary>
        NoProgress,

        /// <summary>
        /// 不确定进度（滚动条模式）
        /// </summary>
        Indeterminate,

        /// <summary>
        /// 正常进度（绿色）
        /// </summary>
        Normal,

        /// <summary>
        /// 错误进度（红色）
        /// </summary>
        Error,

        /// <summary>
        /// 暂停进度（黄色）
        /// </summary>
        Paused
    }
}
