using System;
using System.Drawing;
using System.Windows.Forms;

namespace NindoLauncher;

public class SettingsDialog : Form
{
    private readonly LauncherConfig _config;
    private readonly TextBox _serverHostBox;
    private readonly TextBox _serverPortBox;
    private readonly TextBox _updateUrlBox;
    private readonly CheckBox _closeOnLaunchCheck;

    public SettingsDialog(LauncherConfig config)
    {
        _config = config;

        Text = "Settings";
        Size = new Size(420, 280);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(30, 34, 44);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9.5f);

        int y = 15;

        AddLabel("Server Address:", 15, y);
        _serverHostBox = AddTextBox(config.ServerHost, 150, y, 230);
        y += 35;

        AddLabel("Server Port:", 15, y);
        _serverPortBox = AddTextBox(config.ServerPort.ToString(), 150, y, 80);
        y += 35;

        AddLabel("Update URL:", 15, y);
        _updateUrlBox = AddTextBox(config.UpdateUrl, 150, y, 230);
        y += 35;

        _closeOnLaunchCheck = new CheckBox
        {
            Text = "Close launcher after game starts",
            Checked = config.CloseOnLaunch,
            Location = new Point(15, y),
            AutoSize = true,
            ForeColor = Color.FromArgb(190, 200, 215),
        };
        Controls.Add(_closeOnLaunchCheck);
        y += 40;

        var saveBtn = new Button
        {
            Text = "Save",
            DialogResult = DialogResult.OK,
            BackColor = Color.FromArgb(60, 130, 200),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(200, y),
            Size = new Size(90, 32),
        };
        saveBtn.FlatAppearance.BorderSize = 0;
        saveBtn.Click += (_, _) =>
        {
            _config.ServerHost = _serverHostBox.Text.Trim();
            if (int.TryParse(_serverPortBox.Text, out int port) && port > 0 && port <= 65535)
                _config.ServerPort = port;
            _config.UpdateUrl = _updateUrlBox.Text.Trim();
            _config.CloseOnLaunch = _closeOnLaunchCheck.Checked;
        };
        Controls.Add(saveBtn);

        var cancelBtn = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            BackColor = Color.FromArgb(60, 64, 76),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Location = new Point(300, y),
            Size = new Size(90, 32),
        };
        cancelBtn.FlatAppearance.BorderSize = 0;
        Controls.Add(cancelBtn);

        AcceptButton = saveBtn;
        CancelButton = cancelBtn;
    }

    private Label AddLabel(string text, int x, int y)
    {
        var lbl = new Label
        {
            Text = text,
            Location = new Point(x, y + 3),
            AutoSize = true,
            ForeColor = Color.FromArgb(170, 180, 195),
        };
        Controls.Add(lbl);
        return lbl;
    }

    private TextBox AddTextBox(string value, int x, int y, int width)
    {
        var box = new TextBox
        {
            Text = value,
            Location = new Point(x, y),
            Size = new Size(width, 24),
            BackColor = Color.FromArgb(44, 50, 62),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
        };
        Controls.Add(box);
        return box;
    }
}
