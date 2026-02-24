using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

const string CLIENT_VERSION = "1.0.0";
const string GITHUB_REPO = "puma133/insurgency-acx";
const string UPDATE_ASSET_NAME = "insurgency_acx.exe";
const int CLIENT_UDP_CONFIG_PORT = 8766;
const string FIREWALL_RULE_NAME = "Insurgency AC-X";
const string FIREWALL_MARKER_FILE = ".firewall_rule_added";
const string DEFAULT_RECEIVER_URL = "http://185.170.212.151:8765";

#if DEBUG
Logger.DebugMode = true;
#endif

string configPath = Path.Combine(AppContext.BaseDirectory, "config.ini");

Logger.Debug("AC-X client starting.");

EnsureFirewallRuleOnFirstRun();

void EnsureFirewallRuleOnFirstRun()
{
    if (!IsRunAsAdmin()) return;
    var markerPath = Path.Combine(AppContext.BaseDirectory, FIREWALL_MARKER_FILE);
    if (File.Exists(markerPath)) return;
    var exePath = Environment.ProcessPath ?? Application.ExecutablePath;
    if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;
    try
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netsh",
            Arguments = $"advfirewall firewall add rule name=\"{FIREWALL_RULE_NAME}\" dir=in action=allow program=\"{exePath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            Verb = "runas"
        };
        using var p = Process.Start(psi);
        if (p != null && p.WaitForExit(5000) && p.ExitCode == 0)
        {
            try { File.WriteAllText(markerPath, DateTime.Now.ToString("O")); } catch { }
            Logger.Debug("Firewall rule added.");
        }
    }
    catch (Exception ex)
    {
        Logger.Debug($"Firewall rule error: {ex.Message}");
    }
}

static bool IsRunAsAdmin()
{
    try
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
    catch { return false; }
}

static string? DetectSteamIdFromRegistry()
{
    try
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam\ActiveProcess");
        if (key == null) return null;
        var val = key.GetValue("ActiveUser");
        if (val == null) return null;
        uint accountId = unchecked((uint)Convert.ToInt32(val));
        if (accountId == 0) return null;
        int y = (int)(accountId & 1);
        uint z = accountId >> 1;
        return $"STEAM_1:{y}:{z}";
    }
    catch { return null; }
}

static void UpdateSteamIdInConfig(string path, string steamId)
{
    try
    {
        if (!File.Exists(path)) return;
        var lines = File.ReadAllLines(path);
        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("steam_id", StringComparison.OrdinalIgnoreCase) && trimmed.Contains('='))
            {
                lines[i] = $"steam_id = {steamId}";
                File.WriteAllLines(path, lines);
                return;
            }
        }
    }
    catch { }
}

static void CreateDefaultConfig(string path, string steamId)
{
    try
    {
        var content = $"""
            [ac_screenshot]
            steam_id = {steamId}
            jpeg_quality = 75
            max_width = 1280
            """;
        File.WriteAllText(path, content.Replace("            ", ""));
    }
    catch { }
}

static async Task CheckForUpdateAsync()
{
    try
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("AC-X-Updater");
        http.Timeout = TimeSpan.FromSeconds(10);

        var json = await http.GetStringAsync($"https://api.github.com/repos/{GITHUB_REPO}/releases/latest");

        var tagMatch = Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([^\"]+)\"");
        if (!tagMatch.Success) return;
        var remoteVersion = tagMatch.Groups[1].Value;

        if (!IsNewerVersion(remoteVersion, CLIENT_VERSION)) return;

        string? downloadUrl = null;
        var assetsMatch = Regex.Match(json, "\"assets\"\\s*:\\s*\\[([\\s\\S]*?)\\]");
        if (assetsMatch.Success)
        {
            var assetsBlock = assetsMatch.Groups[1].Value;
            var urlPattern = new Regex("\"browser_download_url\"\\s*:\\s*\"([^\"]*" + Regex.Escape(UPDATE_ASSET_NAME) + ")\"");
            var urlMatch = urlPattern.Match(assetsBlock);
            if (urlMatch.Success)
                downloadUrl = urlMatch.Groups[1].Value;
        }

        if (downloadUrl == null) return;

        Logger.Log($"Доступна новая версия: v{remoteVersion} (текущая: v{CLIENT_VERSION})");
        Logger.Log("Загрузка обновления...");

        var bytes = await http.GetByteArrayAsync(downloadUrl);
        var currentExe = Environment.ProcessPath ?? Application.ExecutablePath;
        var updatePath = currentExe + ".update";
        var backupPath = currentExe + ".bak";

        await File.WriteAllBytesAsync(updatePath, bytes);

        var script = Path.Combine(Path.GetTempPath(), "acx_update.cmd");
        File.WriteAllText(script,
            $"""
            @echo off
            timeout /t 2 /nobreak >nul
            if exist "{backupPath}" del /f "{backupPath}"
            move /y "{currentExe}" "{backupPath}"
            move /y "{updatePath}" "{currentExe}"
            start "" "{currentExe}"
            del /f "%~f0"
            """);

        Logger.Log("Обновление загружено. Перезапуск...");
        await Task.Delay(1500);

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{script}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });

        Environment.Exit(0);
    }
    catch (Exception ex)
    {
        Logger.Debug($"Update check error: {ex.Message}");
    }
}

static bool IsNewerVersion(string remote, string local)
{
    var rParts = remote.Split('.', '-');
    var lParts = local.Split('.', '-');
    int max = Math.Max(rParts.Length, lParts.Length);
    for (int i = 0; i < max; i++)
    {
        int r = i < rParts.Length && int.TryParse(rParts[i], out var rv) ? rv : 0;
        int l = i < lParts.Length && int.TryParse(lParts[i], out var lv) ? lv : 0;
        if (r > l) return true;
        if (r < l) return false;
    }
    return false;
}

var steamIdAuto = DetectSteamIdFromRegistry();

if (!File.Exists(configPath))
    CreateDefaultConfig(configPath, steamIdAuto ?? "");

var config = LoadConfig(configPath);
if (config == null)
{
    MessageBox.Show("Ошибка конфигурации.", "AC-X", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    return 1;
}

if (steamIdAuto != null)
{
    if (!string.Equals(config.SteamId, steamIdAuto, StringComparison.Ordinal))
    {
        config = config with { SteamId = steamIdAuto };
        UpdateSteamIdInConfig(configPath, steamIdAuto);
    }
}

var form = new MainForm(config.SteamId);
var cts = new CancellationTokenSource();
Logger.LogToUi = (line) =>
{
    if (form.IsDisposed) return;
    try { form.Invoke(() => form.AppendLog(line)); } catch { }
};

form.FormClosing += (_, _) => cts.Cancel();

form.Shown += (_, _) =>
{
    Logger.Log($"AC-X v{CLIENT_VERSION}");

    if (!string.IsNullOrEmpty(config.SteamId))
        Logger.Log($"Аккаунт: {config.SteamId}");
    else
        Logger.Log("Steam ID не определён. Запустите Steam.");

    Logger.Log("Поиск игры...");

    _ = Task.Run(() => CheckForUpdateAsync());
    _ = Task.Run(() => UdpConfigListener(cts.Token));
    _ = Task.Run(() => HeartbeatLoop(config, cts.Token));
    _ = Task.Run(() => GameWatcherLoop(form, cts.Token));
    Task.Run(async () =>
    {
        try { await RunWorkerAsync(config, cts.Token); }
        catch (Exception ex) { Logger.Debug($"Worker error: {ex.Message}"); }
    }, cts.Token);
};

Application.Run(form);
return 0;

static async Task GameWatcherLoop(MainForm form, CancellationToken cancel)
{
    string? lastGame = null;
    while (!cancel.IsCancellationRequested)
    {
        string? current = null;
        foreach (var name in new[] { "insurgency_x64", "insurgency" })
        {
            var procs = Process.GetProcessesByName(name);
            if (procs.Length > 0)
            {
                current = name + ".exe";
                foreach (var p in procs) p.Dispose();
                break;
            }
            foreach (var p in procs) p.Dispose();
        }

        if (current != lastGame)
        {
            lastGame = current;
            if (current != null)
            {
                Logger.Log($"Игра обнаружена: {current}");
                try { form.Invoke(() => form.SetGameStatus(current)); } catch { }
            }
            else
            {
                Logger.Log("Игра не запущена.");
                try { form.Invoke(() => form.SetGameStatus(null)); } catch { }
            }
        }
        await Task.Delay(3000, cancel).ConfigureAwait(false);
    }
}

async Task RunWorkerAsync(Config config, CancellationToken cancel)
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

    if (!string.IsNullOrEmpty(config.GamePath))
    {
        var (exePath, workDir) = ResolveGameExe(config.GamePath);
        if (config.LaunchGame && !string.IsNullOrEmpty(exePath))
        {
            Logger.Log($"Запуск игры: {exePath}");
            LaunchGame(exePath, workDir ?? "");
            await Task.Delay(3000, cancel);
        }
    }

    if (config.WaitForGame || (!string.IsNullOrEmpty(config.GamePath) && config.LaunchGame))
    {
        while (!IsGameRunning() && !cancel.IsCancellationRequested)
            await Task.Delay(2000, cancel);
    }

    if (config.IntervalSec > 0)
    {
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                var bytes = CaptureAndCompress(config.JpegQuality, config.MaxWidth);
                Upload(config, http, bytes);
            }
            catch (Exception ex) { Logger.Debug($"Capture error: {ex.Message}"); }
            await Task.Delay(TimeSpan.FromSeconds(config.IntervalSec), cancel);
        }
    }
    else
    {
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                var direct = DirectServerConfigState.Get();
                if (direct != null)
                {
                    var httpConfig = new Config(direct.ReceiverUrl, direct.SteamId, config.Token, config.IntervalSec, config.JpegQuality, config.MaxWidth, config.GamePath, config.WaitForGame, config.LaunchGame);
                    if (PollRequested(httpConfig, http))
                    {
                        var bytes = CaptureAndCompress(config.JpegQuality, config.MaxWidth);
                        if (Upload(httpConfig, http, bytes))
                            ClearRequested(httpConfig, http);
                    }
                }
                else if (!string.IsNullOrEmpty(config.SteamId) && config.SteamId.StartsWith("STEAM_", StringComparison.OrdinalIgnoreCase))
                {
                    if (PollRequested(config, http))
                    {
                        var bytes = CaptureAndCompress(config.JpegQuality, config.MaxWidth);
                        if (Upload(config, http, bytes))
                            ClearRequested(config, http);
                    }
                }
            }
            catch (Exception ex) { Logger.Debug($"Poll error: {ex.Message}"); }
            await Task.Delay(TimeSpan.FromSeconds(10), cancel);
        }
    }
}

static async Task UdpConfigListener(CancellationToken cancel)
{
    try
    {
        using var udp = new UdpClient(CLIENT_UDP_CONFIG_PORT);
        Logger.Debug($"UDP listener on port {CLIENT_UDP_CONFIG_PORT}");
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(cancel);
                var msg = Encoding.UTF8.GetString(result.Buffer).Trim();
                var parts = msg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                string? receiverUrl = null;
                string? steamId = null;
                if (parts.Length == 2 && parts[1].StartsWith("STEAM_", StringComparison.OrdinalIgnoreCase))
                {
                    receiverUrl = parts[0].TrimEnd('/');
                    steamId = parts[1];
                }
                else if (parts.Length >= 3 && parts[2].StartsWith("STEAM_", StringComparison.OrdinalIgnoreCase) && int.TryParse(parts[1], out int port))
                {
                    receiverUrl = $"http://{parts[0]}:{port}";
                    steamId = parts[2];
                }
                if (receiverUrl != null && steamId != null)
                {
                    DirectServerConfigState.Set(new ServerConfig(receiverUrl, steamId));
                    Logger.Debug($"Server config: {receiverUrl}, {steamId}");
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { Logger.Debug($"UDP: {ex.Message}"); }
        }
    }
    catch (Exception ex) { Logger.Debug($"UDP listener: {ex.Message}"); }
}

static async Task HeartbeatLoop(Config config, CancellationToken cancel)
{
    using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    const int intervalSec = 10;
    bool firstOk = true;
    while (!cancel.IsCancellationRequested)
    {
        try
        {
            string? url = null;
            string? steamId = null;

            var direct = DirectServerConfigState.Get();
            if (direct != null)
            {
                url = direct.ReceiverUrl;
                steamId = direct.SteamId;
            }
            else if (!string.IsNullOrEmpty(config.ReceiverUrl) &&
                     !string.IsNullOrEmpty(config.SteamId) &&
                     config.SteamId.StartsWith("STEAM_", StringComparison.OrdinalIgnoreCase))
            {
                url = config.ReceiverUrl;
                steamId = config.SteamId;
            }

            if (url != null && steamId != null)
            {
                using var content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["steamid"] = steamId,
                    ["token"] = config.Token ?? ""
                });
                Logger.Debug($"Heartbeat -> {url}/heartbeat (steamid={steamId})");
                var resp = await http.PostAsync($"{url}/heartbeat", content, cancel);
                if (resp.IsSuccessStatusCode)
                {
                    if (firstOk)
                    {
                        Logger.Log("Подключено к серверу AC-X.");
                        firstOk = false;
                    }
                    Logger.Debug($"Heartbeat OK ({steamId})");
                }
                else
                    Logger.Log($"Ошибка связи с сервером: {(int)resp.StatusCode}");
            }
            else
            {
                Logger.Debug($"Heartbeat skip: url={url ?? "null"}, steamId={steamId ?? "null"}");
            }
        }
        catch (OperationCanceledException) { break; }
        catch (Exception ex) { Logger.Log($"Heartbeat ошибка: {ex.Message}"); }

        await Task.Delay(TimeSpan.FromSeconds(intervalSec), cancel);
    }
}

static bool IsGameRunning()
{
    return Process.GetProcessesByName("insurgency_x64").Length > 0
        || Process.GetProcessesByName("insurgency").Length > 0;
}

static (string? exePath, string? workDir) ResolveGameExe(string gamePath)
{
    gamePath = gamePath.Trim().TrimEnd('\\', '/');
    if (string.IsNullOrEmpty(gamePath)) return (null, null);
    if (gamePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(gamePath))
        return (gamePath, Path.GetDirectoryName(gamePath) ?? "");
    var dir = Directory.Exists(gamePath) ? gamePath : Path.GetDirectoryName(gamePath);
    if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return (null, null);
    var x64 = Path.Combine(dir, "insurgency_x64.exe");
    if (File.Exists(x64)) return (x64, dir);
    var x86 = Path.Combine(dir, "insurgency.exe");
    if (File.Exists(x86)) return (x86, dir);
    return (null, null);
}

static bool LaunchGame(string exePath, string workDir)
{
    try
    {
        Process.Start(new ProcessStartInfo { FileName = exePath, WorkingDirectory = workDir, UseShellExecute = true });
        return true;
    }
    catch { return false; }
}

static Config? LoadConfig(string path)
{
    if (!File.Exists(path))
        return new Config(DEFAULT_RECEIVER_URL, "", "", 0, 75, 1280, "", false, false);

    var lines = File.ReadAllLines(path);
    var section = false;
    string? steamId = null, token = null, gamePath = null;
    int intervalSec = 0, jpegQuality = 75, maxWidth = 1280;
    bool waitForGame = false, launchGame = false;

    foreach (var line in lines)
    {
        var s = line.Trim();
        if (s.StartsWith("[") && s.EndsWith("]"))
        {
            section = string.Equals(s, "[ac_screenshot]", StringComparison.OrdinalIgnoreCase);
            continue;
        }
        if (!section || string.IsNullOrEmpty(s) || s.StartsWith(";")) continue;
        var eq = s.IndexOf('=');
        if (eq <= 0) continue;
        var key = s[..eq].Trim();
        var value = s[(eq + 1)..].Trim();
        switch (key.ToLowerInvariant())
        {
            case "steam_id": steamId = value; break;
            case "token": token = value; break;
            case "interval_sec": int.TryParse(value, out intervalSec); break;
            case "jpeg_quality": int.TryParse(value, out jpegQuality); break;
            case "max_width": int.TryParse(value, out maxWidth); break;
            case "game_path": gamePath = value; break;
            case "wait_for_game": waitForGame = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
            case "launch_game": launchGame = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase); break;
        }
    }

    jpegQuality = Math.Clamp(jpegQuality, 1, 95);
    return new Config(DEFAULT_RECEIVER_URL, steamId ?? "", token ?? "", intervalSec, jpegQuality, maxWidth, gamePath ?? "", waitForGame, launchGame);
}

static byte[] CaptureAndCompress(int quality, int maxWidth)
{
    Rectangle bounds;
    var hwnd = FindGameWindow();
    if (hwnd != IntPtr.Zero && !IsIconic(hwnd) && GetWindowRect(hwnd, out RECT wr))
    {
        int bw = wr.Right - wr.Left;
        int bh = wr.Bottom - wr.Top;
        if (bw > 0 && bh > 0)
            bounds = new Rectangle(wr.Left, wr.Top, bw, bh);
        else
        {
            var (sw, sh) = GetPrimaryScreenSize();
            bounds = new Rectangle(0, 0, sw, sh);
        }
    }
    else
    {
        var (sw, sh) = GetPrimaryScreenSize();
        bounds = new Rectangle(0, 0, sw, sh);
    }

    using var bmp = new Bitmap(bounds.Width, bounds.Height);
    using (var g = Graphics.FromImage(bmp))
        g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);

    if (maxWidth > 0 && bmp.Width > maxWidth)
    {
        var ratio = (double)maxWidth / bmp.Width;
        var newH = (int)(bmp.Height * ratio);
        using var resized = new Bitmap(maxWidth, newH);
        using (var g = Graphics.FromImage(resized))
            g.DrawImage(bmp, 0, 0, maxWidth, newH);
        return ToJpegBytes(resized, quality);
    }
    return ToJpegBytes(bmp, quality);
}

static IntPtr FindGameWindow()
{
    foreach (var name in new[] { "insurgency_x64", "insurgency" })
    {
        var procs = Process.GetProcessesByName(name);
        foreach (var p in procs)
        {
            if (p.MainWindowHandle != IntPtr.Zero)
            {
                var h = p.MainWindowHandle;
                p.Dispose();
                return h;
            }
            p.Dispose();
        }
    }
    return IntPtr.Zero;
}

static byte[] ToJpegBytes(Bitmap bmp, int quality)
{
    var codec = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
    var encoderParams = new EncoderParameters(1);
    using var qualityParam = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
    encoderParams.Param[0] = qualityParam;
    using var ms = new MemoryStream();
    bmp.Save(ms, codec, encoderParams);
    return ms.ToArray();
}

static bool Upload(Config config, HttpClient http, byte[] imageBytes)
{
    var url = $"{config.ReceiverUrl}/upload";
    using var content = new MultipartFormDataContent();
    content.Add(new ByteArrayContent(imageBytes) { Headers = { ContentType = new MediaTypeHeaderValue("image/jpeg") } }, "file", "screenshot.jpg");
    content.Add(new StringContent(config.SteamId), "steamid");
    if (!string.IsNullOrEmpty(config.Token))
        content.Add(new StringContent(config.Token), "token");
    var response = http.PostAsync(url, content).GetAwaiter().GetResult();
    if (response.IsSuccessStatusCode) return true;
    Logger.Debug($"Upload failed: {(int)response.StatusCode}");
    return false;
}

static bool PollRequested(Config config, HttpClient http)
{
    var url = $"{config.ReceiverUrl}/requested";
    using var req = new HttpRequestMessage(HttpMethod.Get, url);
    if (!string.IsNullOrEmpty(config.Token))
        req.Headers.Add("X-API-Key", config.Token);
    var response = http.SendAsync(req).GetAwaiter().GetResult();
    if (!response.IsSuccessStatusCode) return false;
    var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    var match = Regex.Match(json, @"\""steamids\""\s*:\s*\[(.*?)\]");
    if (!match.Success) return false;
    return match.Groups[1].Value.Contains(config.SteamId, StringComparison.Ordinal);
}

static void ClearRequested(Config config, HttpClient http)
{
    try
    {
        http.DeleteAsync($"{config.ReceiverUrl}/requested/{config.SteamId}").GetAwaiter().GetResult();
    }
    catch { }
}

[DllImport("user32.dll")]
static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

[DllImport("user32.dll")]
static extern bool IsIconic(IntPtr hWnd);

[DllImport("user32.dll")]
static extern int GetSystemMetrics(int nIndex);

static (int Width, int Height) GetPrimaryScreenSize()
{
    int w = GetSystemMetrics(0);
    int h = GetSystemMetrics(1);
    return (w > 0 ? w : 1920, h > 0 ? h : 1080);
}

[StructLayout(LayoutKind.Sequential)]
struct RECT { public int Left, Top, Right, Bottom; }

internal record Config(string ReceiverUrl, string SteamId, string Token, int IntervalSec, int JpegQuality, int MaxWidth, string GamePath, bool WaitForGame, bool LaunchGame);
internal record ServerConfig(string ReceiverUrl, string SteamId);

static class DirectServerConfigState
{
    public static readonly object Lock = new();
    static ServerConfig? s_config;
    public static ServerConfig? Get() { lock (Lock) return s_config; }
    public static void Set(ServerConfig? c) { lock (Lock) s_config = c; }
}

class MainForm : Form
{
    readonly RichTextBox _logBox;
    readonly Label _lblSteamId;
    readonly Label _lblGameStatus;
    readonly Panel _statusBar;

    public MainForm(string steamId)
    {
        Text = "AC-X  |  Insurgency Anti-Cheat";
        Size = new Size(560, 420);
        MinimumSize = new Size(440, 300);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        BackColor = Color.FromArgb(18, 18, 22);
        ForeColor = Color.FromArgb(220, 220, 230);
        Font = new Font("Segoe UI", 9.5f);

        var headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 70,
            BackColor = Color.FromArgb(24, 24, 32),
            Padding = new Padding(16, 10, 16, 10)
        };

        var lblTitle = new Label
        {
            Text = "AC-X",
            Font = new Font("Segoe UI", 18f, FontStyle.Bold),
            ForeColor = Color.FromArgb(100, 180, 255),
            AutoSize = true,
            Location = new Point(14, 6)
        };

        _lblSteamId = new Label
        {
            Text = string.IsNullOrEmpty(steamId) ? "Steam ID: ожидание..." : $"Steam ID: {steamId}",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(160, 165, 180),
            AutoSize = true,
            Location = new Point(16, 42)
        };

        headerPanel.Controls.Add(lblTitle);
        headerPanel.Controls.Add(_lblSteamId);

        _statusBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 36,
            BackColor = Color.FromArgb(24, 24, 32),
            Padding = new Padding(16, 0, 16, 0)
        };

        _lblGameStatus = new Label
        {
            Text = "Игра: поиск...",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(180, 180, 190),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };

        _statusBar.Controls.Add(_lblGameStatus);

        _logBox = new RichTextBox
        {
            ReadOnly = true,
            Dock = DockStyle.Fill,
            Font = new Font("Cascadia Mono, Consolas", 9f),
            BackColor = Color.FromArgb(22, 22, 28),
            ForeColor = Color.FromArgb(190, 195, 210),
            BorderStyle = BorderStyle.None,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Margin = new Padding(0),
            DetectUrls = false
        };

        var logPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 8, 12, 8),
            BackColor = Color.FromArgb(18, 18, 22)
        };
        logPanel.Controls.Add(_logBox);

        Controls.Add(logPanel);
        Controls.Add(_statusBar);
        Controls.Add(headerPanel);
    }

    public void AppendLog(string line)
    {
        if (_logBox.IsDisposed) return;

        Color color;
        if (line.Contains("Ошибка") || line.Contains("error", StringComparison.OrdinalIgnoreCase))
            color = Color.FromArgb(255, 100, 100);
        else if (line.Contains("отправлен") || line.Contains("Подключено") || line.Contains("обнаружена"))
            color = Color.FromArgb(100, 220, 130);
        else if (line.Contains("Steam ID") || line.Contains("Аккаунт"))
            color = Color.FromArgb(100, 180, 255);
        else
            color = Color.FromArgb(190, 195, 210);

        _logBox.SelectionStart = _logBox.TextLength;
        _logBox.SelectionLength = 0;
        _logBox.SelectionColor = color;
        _logBox.AppendText(line + Environment.NewLine);
        _logBox.ScrollToCaret();
    }

    public void SetGameStatus(string? processName)
    {
        if (processName != null)
        {
            _lblGameStatus.Text = $"Игра: {processName}";
            _lblGameStatus.ForeColor = Color.FromArgb(100, 220, 130);
        }
        else
        {
            _lblGameStatus.Text = "Игра: не запущена";
            _lblGameStatus.ForeColor = Color.FromArgb(180, 100, 100);
        }
    }
}
