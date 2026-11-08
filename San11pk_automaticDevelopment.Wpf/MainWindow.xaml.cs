using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using San11pk_automaticDevelopment.Wpf.Interop;
using San11pk_automaticDevelopment.Wpf.Models;
using San11pk_automaticDevelopment.Wpf.Services;

namespace San11pk_automaticDevelopment.Wpf;

public partial class MainWindow : Window
{
    private const int HotkeyStop = 1100;
    private const int HotkeyFirstStep = 1101;
    private readonly List<int> _registeredHotkeys = [];
    private readonly string _calibrationFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "San11pk_automaticDevelopment.Wpf", "calibration.json");

    private HwndSource? _windowSource;
    private CalibrationProfile _profile = new();
    private CancellationTokenSource? _runCancellation;
    private bool _hotkeysAvailable;

    public MainWindow()
    {
        InitializeComponent();
        LoadCalibration();
        UpdateCalibrationUi();
        Log("起動しました。初期状態はドライラン（ゲームに入力しない）です。");
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        nint windowHandle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(windowHandle);
        _windowSource?.AddHook(WindowMessageHook);

        uint modifiers = Win32.MOD_CONTROL | Win32.MOD_SHIFT | Win32.MOD_NOREPEAT;
        foreach (int id in new[] { HotkeyStop, 1101, 1102, 1103, 1104 })
        {
            uint key = (uint)('0' + (id - HotkeyStop));
            if (!Win32.RegisterHotKey(windowHandle, id, modifiers, key))
            {
                foreach (int registered in _registeredHotkeys)
                    Win32.UnregisterHotKey(windowHandle, registered);
                _registeredHotkeys.Clear();
                Log("Ctrl+Shift+数字の登録に失敗しました。他のアプリとの競合を確認してください。");
                return;
            }
            _registeredHotkeys.Add(id);
        }

        _hotkeysAvailable = true;
        Log("Ctrl+Shift+1～4: 位置記録 / Ctrl+Shift+0: 緊急停止");
    }

    private nint WindowMessageHook(nint hWnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != Win32.WM_HOTKEY)
            return 0;

        int id = (int)wParam;
        if (id == HotkeyStop)
        {
            Stop();
            handled = true;
        }
        else if (id >= HotkeyFirstStep && id < HotkeyFirstStep + 4)
        {
            CapturePosition(id - HotkeyStop);
            handled = true;
        }

        return 0;
    }

    private void CapturePosition(int step)
    {
        if (_runCancellation is not null)
        {
            Log("テスト実行中は座標を記録できません。");
            return;
        }

        string processName = GameWindowService.NormalizeProcessName(ProcessNameTextBox.Text);
        if (string.IsNullOrWhiteSpace(processName) ||
            !GameWindowService.TryGetForegroundGame(processName, out nint gameHandle, out _))
        {
            Log("座標記録を中止: 対象のゲームを最前面にしてください。プロセス名も確認してください。");
            return;
        }

        if (!Win32.GetClientRect(gameHandle, out var clientRect) ||
            clientRect.Width <= 0 || clientRect.Height <= 0 ||
            !Win32.GetCursorPos(out var cursor) ||
            !Win32.ScreenToClient(gameHandle, ref cursor))
        {
            Log("ゲームの描画領域またはカーソル位置を取得できませんでした。");
            return;
        }

        if (cursor.X < 0 || cursor.Y < 0 ||
            cursor.X >= clientRect.Width || cursor.Y >= clientRect.Height)
        {
            Log("カーソルがゲームの描画領域外です。");
            return;
        }

        if (!string.Equals(_profile.ProcessName, processName, StringComparison.OrdinalIgnoreCase) ||
            _profile.ClientWidth != clientRect.Width ||
            _profile.ClientHeight != clientRect.Height)
        {
            _profile = new CalibrationProfile
            {
                ProcessName = processName,
                ClientWidth = clientRect.Width,
                ClientHeight = clientRect.Height
            };
            Log("プロセス名または画面サイズが変わったため、記録した座標を初期化しました。");
        }

        var point = new ClickPoint { X = cursor.X, Y = cursor.Y };
        _profile.SetPoint(step, point);
        SaveCalibration();
        UpdateCalibrationUi();
        Log($"座標 {step} を記録: {point}");
    }

    private void UpdateCalibrationUi()
    {
        Step1Text.Text = _profile.ContextMenu?.ToString() ?? "未設定";
        Step2Text.Text = _profile.DomesticMenu?.ToString() ?? "未設定";
        Step3Text.Text = _profile.RecruitMenu?.ToString() ?? "未設定";
        Step4Text.Text = _profile.ConfirmButton?.ToString() ?? "未設定";
        ResolutionText.Text = _profile.ClientWidth > 0
            ? $"基準サイズ：{_profile.ClientWidth} × {_profile.ClientHeight} / {_profile.ProcessName}"
            : "基準サイズ：未設定";
    }

    private void LoadCalibration()
    {
        try
        {
            if (!File.Exists(_calibrationFile))
                return;
            _profile = JsonSerializer.Deserialize<CalibrationProfile>(
                           File.ReadAllText(_calibrationFile)) ?? new CalibrationProfile();
            ProcessNameTextBox.Text = _profile.ProcessName;
        }
        catch (Exception ex)
        {
            _profile = new CalibrationProfile();
            Log("設定の読み込みに失敗: " + ex.Message);
        }
    }

    private void SaveCalibration()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_calibrationFile)!);
            File.WriteAllText(_calibrationFile,
                JsonSerializer.Serialize(_profile, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log("設定の保存に失敗: " + ex.Message);
        }
    }

    private void ClearCalibration_Click(object sender, RoutedEventArgs e)
    {
        if (_runCancellation is not null)
        {
            Log("処理中は設定を変更できません。");
            return;
        }
        _profile = new CalibrationProfile
        {
            ProcessName = GameWindowService.NormalizeProcessName(ProcessNameTextBox.Text)
        };
        SaveCalibration();
        UpdateCalibrationUi();
        Log("記録座標を消去しました。");
    }

    private async void RunOne_Click(object sender, RoutedEventArgs e) => await RunStepsAsync(1);
    private async void RunThree_Click(object sender, RoutedEventArgs e) => await RunStepsAsync(3);
    private async void RunFour_Click(object sender, RoutedEventArgs e) => await RunStepsAsync(4);

    private async Task RunStepsAsync(int stepCount)
    {
        if (_runCancellation is not null)
        {
            Log("すでにテスト中です。");
            return;
        }

        string processName = GameWindowService.NormalizeProcessName(ProcessNameTextBox.Text);
        if (string.IsNullOrWhiteSpace(processName) ||
            !string.Equals(_profile.ProcessName, processName, StringComparison.OrdinalIgnoreCase))
        {
            Log("プロセス名が記録時と異なります。正しいゲーム名で座標を記録してください。");
            return;
        }

        if (!GameWindowService.TryFindGame(processName, out nint gameHandle, out uint gamePid))
        {
            Log("対象ゲームのウィンドウを発見できません。起動状態とプロセス名を確認してください。");
            return;
        }

        if (!Win32.GetClientRect(gameHandle, out var clientRect) ||
            clientRect.Width != _profile.ClientWidth ||
            clientRect.Height != _profile.ClientHeight)
        {
            Log("画面サイズが記録時と異なります。座標を取り直してください。");
            return;
        }

        for (int step = 1; step <= stepCount; step++)
        {
            if (_profile.GetPoint(step) is not { } point ||
                point.X < 0 || point.Y < 0 ||
                point.X >= clientRect.Width || point.Y >= clientRect.Height)
            {
                Log($"手順 {step} の有効な座標がありません。先に記録してください。");
                return;
            }
        }

        if (!int.TryParse(DelayTextBox.Text, out int delayMs) || delayMs < 200 || delayMs > 5000)
        {
            Log("操作間隔は 200～5000 ミリ秒にしてください。");
            return;
        }

        bool sendInput = EnableClickCheckBox.IsChecked == true;
        if (!sendInput)
        {
            Log($"ドライラン（{stepCount} 手順）。ゲームへの入力は行いません。");
            for (int step = 1; step <= stepCount; step++)
                Log($"  {step}: {(step == 1 ? "右クリック" : "左クリック")} / {_profile.GetPoint(step)}");
            return;
        }

        if (!_hotkeysAvailable)
        {
            Log("緊急停止ホットキーが有効でないため、実クリックを拒否しました。");
            return;
        }

        if (stepCount == 4 &&
            MessageBox.Show(this,
                "ゲーム内の「実行」ボタンまでクリックします。募兵が実際に行われる可能性があります。続けますか？",
                "ゲーム内操作の確認", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _runCancellation = cancellation;
        RunStateText.Text = "実行中（Ctrl+Shift+0 で停止）";
        RunOneButton.IsEnabled = false;
        RunThreeButton.IsEnabled = false;
        RunFourButton.IsEnabled = false;

        try
        {
            Log("ゲームにフォーカスを移して開始します。停止: Ctrl+Shift+0");
            Win32.SetForegroundWindow(gameHandle);
            await Task.Delay(600, cancellation.Token);
            EnsureGameForeground(gamePid);

            for (int step = 1; step <= stepCount; step++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                EnsureGameForeground(gamePid);

                var point = _profile.GetPoint(step)!;
                var screenPoint = new Win32.POINT { X = point.X, Y = point.Y };
                if (!Win32.ClientToScreen(gameHandle, ref screenPoint))
                    throw new InvalidOperationException("ゲームの画面座標を変換できません。");

                if (!Win32.SetCursorPos(screenPoint.X, screenPoint.Y))
                    throw new InvalidOperationException("マウス位置を変更できません。");

                await Task.Delay(120, cancellation.Token);
                EnsureGameForeground(gamePid);
                Win32.Click(right: step == 1);
                Log($"手順 {step} 完了：{(step == 1 ? "右" : "左")}クリック {point}");

                if (step < stepCount)
                    await Task.Delay(delayMs, cancellation.Token);
            }

            Log("テスト入力が終了しました。ゲーム内で結果を確認してください。");
        }
        catch (OperationCanceledException)
        {
            Log("緊急停止を受け付けました。");
        }
        catch (Exception ex)
        {
            Log("安全のため処理を停止しました: " + ex.Message);
        }
        finally
        {
            _runCancellation = null;
            RunStateText.Text = "待機中";
            RunOneButton.IsEnabled = true;
            RunThreeButton.IsEnabled = true;
            RunFourButton.IsEnabled = true;
        }
    }

    private static void EnsureGameForeground(uint gamePid)
    {
        if (!GameWindowService.IsGameForeground(gamePid))
            throw new InvalidOperationException("ゲームが最前面でなくなりました。誤クリック防止のため停止しました。");
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => Stop();

    private void Stop()
    {
        if (_runCancellation is null)
        {
            Log("停止操作を受け付けました（現在実行中のテストはありません）。");
            return;
        }

        _runCancellation.Cancel();
        Log("停止要求を受け付けました。");
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LogTextBox.Text))
        {
            RunStateText.Text = "コピーする操作ログがありません。";
            return;
        }

        try
        {
            Clipboard.SetText(LogTextBox.Text);
            RunStateText.Text = "操作ログをクリップボードにコピーしました。";
        }
        catch (Exception ex)
        {
            RunStateText.Text = "ログのコピーに失敗しました。";
            Log("クリップボードへのコピーに失敗: " + ex.Message);
        }
    }

    private void Log(string message)
    {
        if (LogTextBox is null)
            return;
        LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        LogTextBox.ScrollToEnd();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _runCancellation?.Cancel();
        nint hWnd = new WindowInteropHelper(this).Handle;
        foreach (int hotkeyId in _registeredHotkeys)
            Win32.UnregisterHotKey(hWnd, hotkeyId);
        _windowSource?.RemoveHook(WindowMessageHook);
    }
}
