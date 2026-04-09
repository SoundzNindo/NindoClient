using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace NindoLauncher;

public class MainForm : Form
{
    private readonly Label _titleLabel;
    private readonly Label _versionLabel;
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;
    private readonly Button _startButton;
    private readonly Button _settingsButton;
    private readonly Panel _headerPanel;

    private readonly LauncherConfig _config;
    private readonly UpdateManager _updater;
    private bool _updateInProgress;
    private bool _gameReady;

    public MainForm()
    {
        _config = LauncherConfig.Load();
        _updater = new UpdateManager(_config);

        // ── Form setup ──
        Text = "Nindo Launcher";
        Size = new Size(700, 420);
        MinimumSize = new Size(600, 380);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Color.FromArgb(24, 28, 36);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10f);
        DoubleBuffered = true;

        // ── Header panel with title ──
        _headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 160,
            BackColor = Color.FromArgb(18, 22, 30),
        };
        Controls.Add(_headerPanel);

        _titleLabel = new Label
        {
            Text = "NINDO",
            Font = new Font("Segoe UI", 48f, FontStyle.Bold),
            ForeColor = Color.FromArgb(255, 160, 40),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill,
        };
        _headerPanel.Controls.Add(_titleLabel);

        // ── Version label ──
        _versionLabel = new Label
        {
            Text = "Checking for updates...",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(140, 150, 170),
            Location = new Point(20, 175),
            AutoSize = true,
        };
        Controls.Add(_versionLabel);

        // ── Status label ──
        _statusLabel = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(180, 190, 210),
            Location = new Point(20, 290),
            Size = new Size(650, 20),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        Controls.Add(_statusLabel);

        // ── Progress bar ──
        _progressBar = new ProgressBar
        {
            Location = new Point(20, 315),
            Size = new Size(510, 30),
            Style = ProgressBarStyle.Continuous,
            Minimum = 0,
            Maximum = 100,
            Value = 0,
        };
        Controls.Add(_progressBar);

        // ── Start Game button ──
        _startButton = new Button
        {
            Text = "START GAME",
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            BackColor = Color.FromArgb(240, 160, 20),
            ForeColor = Color.FromArgb(30, 30, 30),
            FlatStyle = FlatStyle.Flat,
            Location = new Point(545, 305),
            Size = new Size(130, 48),
            Enabled = false,
            Cursor = Cursors.Hand,
        };
        _startButton.FlatAppearance.BorderSize = 0;
        _startButton.Click += StartButton_Click;
        Controls.Add(_startButton);

        // ── Settings button ──
        _settingsButton = new Button
        {
            Text = "⚙",
            Font = new Font("Segoe UI", 14f),
            BackColor = Color.FromArgb(40, 46, 58),
            ForeColor = Color.FromArgb(160, 170, 185),
            FlatStyle = FlatStyle.Flat,
            Location = new Point(640, 170),
            Size = new Size(35, 35),
            Cursor = Cursors.Hand,
        };
        _settingsButton.FlatAppearance.BorderSize = 0;
        _settingsButton.Click += SettingsButton_Click;
        Controls.Add(_settingsButton);

        // ── Kick off update check ──
        Shown += async (_, _) => await CheckAndUpdate();
    }

    private async Task CheckAndUpdate()
    {
        if (_updateInProgress) return;
        _updateInProgress = true;
        _startButton.Enabled = false;

        try
        {
            _statusLabel.Text = "Checking for updates...";

            var result = await _updater.CheckForUpdates(
                onProgress: (pct, msg) =>
                {
                    if (InvokeRequired)
                        Invoke(() => UpdateProgress(pct, msg));
                    else
                        UpdateProgress(pct, msg);
                });

            if (result.FilesUpdated > 0)
            {
                SetStatus($"Updated {result.FilesUpdated} files (v{result.Version})", Color.FromArgb(100, 220, 120));
            }
            else if (result.UpToDate)
            {
                SetStatus($"Game is up to date (v{result.Version})", Color.FromArgb(100, 220, 120));
            }
            else if (result.Error != null)
            {
                SetStatus($"Update check failed: {result.Error}", Color.FromArgb(255, 120, 100));
            }

            _versionLabel.Text = !string.IsNullOrEmpty(result.Version)
                ? $"Version {result.Version}"
                : "Offline mode";

            _progressBar.Value = 100;
            _gameReady = true;
            _startButton.Enabled = true;
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}", Color.FromArgb(255, 120, 100));
            // Still allow launching if game files exist locally
            if (GameExeExists())
            {
                _gameReady = true;
                _startButton.Enabled = true;
                _versionLabel.Text = "Offline mode";
            }
        }
        finally
        {
            _updateInProgress = false;
        }
    }

    private void UpdateProgress(int percent, string message)
    {
        _progressBar.Value = Math.Clamp(percent, 0, 100);
        _statusLabel.Text = message;
    }

    private void SetStatus(string text, Color color)
    {
        _statusLabel.Text = text;
        _statusLabel.ForeColor = color;
    }

    private void StartButton_Click(object? sender, EventArgs e)
    {
        if (!_gameReady) return;

        string exePath = GetGameExePath();
        if (!File.Exists(exePath))
        {
            MessageBox.Show(
                "Game executable not found.\nPlease check for updates first.",
                "Nindo Launcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"--connect {_config.ServerHost} --port {_config.ServerPort}",
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? ".",
                UseShellExecute = false,
            };

            System.Diagnostics.Process.Start(startInfo);

            // Minimize or close launcher after game starts
            if (_config.CloseOnLaunch)
                Close();
            else
                WindowState = FormWindowState.Minimized;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to launch game:\n{ex.Message}",
                "Nindo Launcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void SettingsButton_Click(object? sender, EventArgs e)
    {
        using var dlg = new SettingsDialog(_config);
        if (dlg.ShowDialog(this) == DialogResult.OK)
        {
            _config.Save();
        }
    }

    private string GetGameExePath()
    {
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, _config.GameDirectory, "NindoRuntime.exe"));
    }

    private bool GameExeExists() => File.Exists(GetGameExePath());
}
