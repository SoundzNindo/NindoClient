using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NindoLauncher;

public class LauncherConfig
{
    private static readonly string ConfigPath = Path.Combine(AppContext.BaseDirectory, "launcher-config.json");

    [JsonPropertyName("serverHost")]
    public string ServerHost { get; set; } = "127.0.0.1";

    [JsonPropertyName("serverPort")]
    public int ServerPort { get; set; } = 1906;

    [JsonPropertyName("updateUrl")]
    public string UpdateUrl { get; set; } = "https://github.com/SoundzNindo/NindoClient/releases/latest/download";

    [JsonPropertyName("gameDirectory")]
    public string GameDirectory { get; set; } = "game";

    [JsonPropertyName("closeOnLaunch")]
    public bool CloseOnLaunch { get; set; } = false;

    public static LauncherConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                return JsonSerializer.Deserialize<LauncherConfig>(json) ?? new LauncherConfig();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Launcher] Failed to load config: {ex.Message}");
        }

        var config = new LauncherConfig();
        config.Save();
        return config;
    }

    public void Save()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Launcher] Failed to save config: {ex.Message}");
        }
    }
}
