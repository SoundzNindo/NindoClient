using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace NindoLauncher;

/// <summary>
/// Handles checking a remote manifest.json against the local one,
/// downloading only the files that have changed.
/// </summary>
public class UpdateManager
{
    private readonly LauncherConfig _config;
    private readonly HttpClient _http;

    public UpdateManager(LauncherConfig config)
    {
        _config = config;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("NindoLauncher/1.0");
    }

    /// <summary>
    /// Determines if the update URL points to GitHub Releases (zip-based updates).
    /// </summary>
    private bool IsGitHubReleasesUrl => !string.IsNullOrWhiteSpace(_config.UpdateUrl)
        && _config.UpdateUrl.Contains("github.com", StringComparison.OrdinalIgnoreCase)
        && _config.UpdateUrl.Contains("releases", StringComparison.OrdinalIgnoreCase);

    public async Task<UpdateResult> CheckForUpdates(Action<int, string>? onProgress = null)
    {
        string gameDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _config.GameDirectory));

        // If no update URL configured, just verify local files exist
        if (string.IsNullOrWhiteSpace(_config.UpdateUrl))
        {
            var localManifest = LoadLocalManifest(gameDir);
            return new UpdateResult
            {
                UpToDate = localManifest != null,
                Version = localManifest?.Version ?? "unknown",
                FilesUpdated = 0,
                Error = localManifest == null ? "No update URL configured and no local game files found" : null,
            };
        }

        if (IsGitHubReleasesUrl)
            return await CheckForUpdatesZip(gameDir, onProgress);
        else
            return await CheckForUpdatesPerFile(gameDir, onProgress);
    }

    /// <summary>
    /// Zip-based update flow for GitHub Releases.
    /// Downloads manifest.json first, compares version, then downloads and extracts game.zip.
    /// </summary>
    private async Task<UpdateResult> CheckForUpdatesZip(string gameDir, Action<int, string>? onProgress)
    {
        onProgress?.Invoke(5, "Fetching remote manifest...");

        string baseUrl = _config.UpdateUrl.TrimEnd('/');
        UpdateManifest remoteManifest;
        try
        {
            string manifestUrl = $"{baseUrl}/manifest.json";
            string json = await _http.GetStringAsync(manifestUrl);
            remoteManifest = JsonSerializer.Deserialize<UpdateManifest>(json)
                ?? throw new Exception("Invalid manifest format");
        }
        catch (Exception ex)
        {
            var localManifest = LoadLocalManifest(gameDir);
            return new UpdateResult
            {
                UpToDate = localManifest != null,
                Version = localManifest?.Version ?? "unknown",
                Error = localManifest != null ? null : $"Cannot reach update server: {ex.Message}",
            };
        }

        onProgress?.Invoke(10, $"Remote version: {remoteManifest.Version}");

        // Compare with local version
        var local = LoadLocalManifest(gameDir);
        if (local != null && string.Equals(local.Version, remoteManifest.Version, StringComparison.OrdinalIgnoreCase))
        {
            onProgress?.Invoke(100, "Game is up to date");
            return new UpdateResult { UpToDate = true, Version = remoteManifest.Version };
        }

        // Download game.zip
        onProgress?.Invoke(15, "Downloading game update...");
        string zipUrl = $"{baseUrl}/game.zip";

        string tempZip = Path.Combine(Path.GetTempPath(), $"nindo_update_{Guid.NewGuid():N}.zip");
        try
        {
            using var response = await _http.GetAsync(zipUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            long totalBytes = response.Content.Headers.ContentLength ?? -1;
            long downloaded = 0;

            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 81920);

            byte[] buffer = new byte[81920];
            int bytesRead;
            while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                downloaded += bytesRead;

                if (totalBytes > 0)
                {
                    int pct = 15 + (int)(70.0 * downloaded / totalBytes);
                    double mb = downloaded / (1024.0 * 1024.0);
                    double totalMb = totalBytes / (1024.0 * 1024.0);
                    onProgress?.Invoke(pct, $"Downloading: {mb:F1} / {totalMb:F1} MB");
                }
            }

            onProgress?.Invoke(85, "Extracting update...");

            // Ensure game directory exists
            if (!Directory.Exists(gameDir))
                Directory.CreateDirectory(gameDir);

            // Extract zip (overwrite existing files)
            ZipFile.ExtractToDirectory(tempZip, gameDir, overwriteFiles: true);

            // Save the remote manifest
            SaveManifest(gameDir, remoteManifest);

            onProgress?.Invoke(100, "Update complete!");

            return new UpdateResult
            {
                UpToDate = true,
                Version = remoteManifest.Version,
                FilesUpdated = remoteManifest.FileCount,
            };
        }
        catch (Exception ex)
        {
            var localManifest = LoadLocalManifest(gameDir);
            return new UpdateResult
            {
                UpToDate = localManifest != null,
                Version = localManifest?.Version ?? "unknown",
                Error = $"Update failed: {ex.Message}",
            };
        }
        finally
        {
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
        }
    }

    /// <summary>
    /// Per-file update flow for custom file servers.
    /// Downloads individual changed files based on manifest hash comparison.
    /// </summary>
    private async Task<UpdateResult> CheckForUpdatesPerFile(string gameDir, Action<int, string>? onProgress)
    {
        onProgress?.Invoke(5, "Fetching remote manifest...");

        // Fetch remote manifest
        UpdateManifest remoteManifest;
        try
        {
            string baseUrl = _config.UpdateUrl.TrimEnd('/');
            string manifestUrl = $"{baseUrl}/manifest.json";
            string json = await _http.GetStringAsync(manifestUrl);
            remoteManifest = JsonSerializer.Deserialize<UpdateManifest>(json)
                ?? throw new Exception("Invalid manifest format");
        }
        catch (Exception ex)
        {
            // Can't reach update server — allow offline play if files exist
            var localManifest = LoadLocalManifest(gameDir);
            return new UpdateResult
            {
                UpToDate = localManifest != null,
                Version = localManifest?.Version ?? "unknown",
                Error = localManifest != null ? null : $"Cannot reach update server: {ex.Message}",
            };
        }

        onProgress?.Invoke(10, $"Remote version: {remoteManifest.Version}");

        // Load local manifest for comparison
        var local = LoadLocalManifest(gameDir);
        var localFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (local?.Files != null)
        {
            foreach (var f in local.Files)
                localFiles[f.Path] = f.Sha256;
        }

        // Determine which files need downloading
        var filesToDownload = new List<ManifestFile>();
        if (remoteManifest.Files != null)
        {
            foreach (var remoteFile in remoteManifest.Files)
            {
                string localPath = Path.Combine(gameDir, remoteFile.Path.Replace('/', Path.DirectorySeparatorChar));
                bool needsDownload = false;

                if (!File.Exists(localPath))
                {
                    needsDownload = true;
                }
                else if (!localFiles.TryGetValue(remoteFile.Path, out string? localHash)
                         || !string.Equals(localHash, remoteFile.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    needsDownload = true;
                }

                if (needsDownload)
                    filesToDownload.Add(remoteFile);
            }
        }

        if (filesToDownload.Count == 0)
        {
            // Save remote manifest locally (version may have changed even if no files did)
            SaveManifest(gameDir, remoteManifest);
            onProgress?.Invoke(100, "Game is up to date");
            return new UpdateResult { UpToDate = true, Version = remoteManifest.Version };
        }

        // Download changed files
        onProgress?.Invoke(15, $"Downloading {filesToDownload.Count} files...");

        string baseDownloadUrl = _config.UpdateUrl.TrimEnd('/');
        int completed = 0;
        int failed = 0;

        foreach (var file in filesToDownload)
        {
            int pct = 15 + (int)(80.0 * completed / filesToDownload.Count);
            onProgress?.Invoke(pct, $"Downloading: {file.Path}");

            string localPath = Path.Combine(gameDir, file.Path.Replace('/', Path.DirectorySeparatorChar));
            string? dir = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            try
            {
                string fileUrl = $"{baseDownloadUrl}/{file.Path}";
                byte[] data = await _http.GetByteArrayAsync(fileUrl);

                // Verify hash before writing
                string downloadHash = ComputeSha256(data);
                if (!string.Equals(downloadHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"[Update] Hash mismatch for {file.Path} - skipping");
                    failed++;
                    continue;
                }

                await File.WriteAllBytesAsync(localPath, data);
                completed++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Update] Failed to download {file.Path}: {ex.Message}");
                failed++;
            }
        }

        // Save updated manifest
        SaveManifest(gameDir, remoteManifest);

        onProgress?.Invoke(100, $"Update complete: {completed} files updated" +
            (failed > 0 ? $" ({failed} failed)" : ""));

        return new UpdateResult
        {
            UpToDate = failed == 0,
            Version = remoteManifest.Version,
            FilesUpdated = completed,
            Error = failed > 0 ? $"{failed} files failed to download" : null,
        };
    }

    private static UpdateManifest? LoadLocalManifest(string gameDir)
    {
        string path = Path.Combine(gameDir, "manifest.json");
        if (!File.Exists(path)) return null;
        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UpdateManifest>(json);
        }
        catch
        {
            return null;
        }
    }

    private static void SaveManifest(string gameDir, UpdateManifest manifest)
    {
        string path = Path.Combine(gameDir, "manifest.json");
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var options = new JsonSerializerOptions { WriteIndented = true };
        string json = JsonSerializer.Serialize(manifest, options);
        File.WriteAllText(path, json);
    }

    private static string ComputeSha256(byte[] data)
    {
        byte[] hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

// ── Data models ──

public class UpdateManifest
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("buildDate")]
    public string BuildDate { get; set; } = "";

    [JsonPropertyName("fileCount")]
    public int FileCount { get; set; }

    [JsonPropertyName("totalSize")]
    public long TotalSize { get; set; }

    [JsonPropertyName("files")]
    public List<ManifestFile>? Files { get; set; }
}

public class ManifestFile
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public class UpdateResult
{
    public bool UpToDate { get; set; }
    public string Version { get; set; } = "";
    public int FilesUpdated { get; set; }
    public string? Error { get; set; }
}
