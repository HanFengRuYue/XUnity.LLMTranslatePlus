using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace XUnity_LLMTranslatePlus.Services
{
    /// <summary>
    /// 热键服务 - 负责向游戏窗口发送键盘输入（ALT+R刷新翻译）
    /// </summary>
    public class HotkeyService
    {
        private readonly LogService _logService;
        private DispatcherQueueTimer? _autoRefreshTimer;
        private bool _isRunning = false;
        private int _intervalSeconds = 5;

        public HotkeyService(LogService logService)
        {
            _logService = logService;
        }

        /// <summary>
        /// 启动自动刷新（定期发送 ALT+R）
        /// </summary>
        /// <param name="intervalSeconds">发送间隔（秒）</param>
        /// <param name="dispatcherQueue">UI线程调度器</param>
        public void StartAutoRefresh(int intervalSeconds, DispatcherQueue dispatcherQueue)
        {
            if (_isRunning)
            {
                _logService.Log("自动刷新已在运行中", LogLevel.Warning);
                return;
            }

            if (intervalSeconds < 1 || intervalSeconds > 60)
            {
                _logService.Log($"自动刷新间隔无效: {intervalSeconds}秒（有效范围 1-60）", LogLevel.Error);
                return;
            }

            _intervalSeconds = intervalSeconds;
            _isRunning = true;

            // 创建定时器
            _autoRefreshTimer = dispatcherQueue.CreateTimer();
            _autoRefreshTimer.Interval = TimeSpan.FromSeconds(_intervalSeconds);
            _autoRefreshTimer.Tick += (s, e) =>
            {
                try
                {
                    SendAltR();
                }
                catch (Exception ex)
                {
                    _logService.Log($"发送 ALT+R 失败: {ex.Message}", LogLevel.Error);
                }
            };

            _autoRefreshTimer.Start();
            _logService.Log($"自动刷新已启动，间隔 {_intervalSeconds} 秒", LogLevel.Info);
        }

        /// <summary>
        /// 停止自动刷新
        /// </summary>
        public void StopAutoRefresh()
        {
            if (!_isRunning)
            {
                return;
            }

            if (_autoRefreshTimer != null)
            {
                _autoRefreshTimer.Stop();
                _autoRefreshTimer = null;
            }

            _isRunning = false;
            _logService.Log("自动刷新已停止", LogLevel.Info);
        }

        /// <summary>
        /// 获取自动刷新运行状态
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// 发送 ALT+R 按键到前台窗口
        /// </summary>
        public void SendAltR()
        {
            try
            {
                // 步骤1: 按下 ALT 键
                SendKey(VK_MENU, false);
                System.Threading.Thread.Sleep(50); // 延迟50ms

                // 步骤2: 按下 R 键
                SendKey((ushort)'R', false);
                System.Threading.Thread.Sleep(50); // 延迟50ms

                // 步骤3: 释放 R 键
                SendKey((ushort)'R', true);
                System.Threading.Thread.Sleep(50); // 延迟50ms

                // 步骤4: 释放 ALT 键
                SendKey(VK_MENU, true);

                _logService.Log("已发送 ALT+R 刷新游戏翻译", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                _logService.Log($"发送 ALT+R 失败: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 发送单个按键事件
        /// </summary>
        /// <param name="keyCode">虚拟键码</param>
        /// <param name="keyUp">true表示释放，false表示按下</param>
        private void SendKey(ushort keyCode, bool keyUp)
        {
            INPUT[] input = new INPUT[1];
            input[0] = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = keyCode,
                        wScan = 0,
                        dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            };

            uint result = SendInput(1, input, Marshal.SizeOf(typeof(INPUT)));
            if (result != 1)
            {
                int error = Marshal.GetLastWin32Error();
                throw new Exception($"SendInput 失败: 错误代码 {error}");
            }
        }

        #region Win32 API 声明

        // 常量
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const ushort VK_MENU = 0x12; // ALT 键

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUT
        {
            [FieldOffset(0)]
            public uint type;
            [FieldOffset(8)]  // 关键：x64 上需要 8 字节对齐，不是 4！
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public KEYBDINPUT ki;
            [FieldOffset(0)]
            public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        #endregion
    }
}
