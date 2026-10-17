using System.IO;
using System.Text.Json;
using SoundBridge.Models;

namespace SoundBridge.Services;

/// <summary>
/// Loads and saves <see cref="AppSettings"/> as JSON in the per-user local app data folder.
/// Kept dependency-free and tolerant: a corrupt or missing file simply resets to defaults.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    public string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoundBridge");

    private string SettingsFilePath => Path.Combine(SettingsDirectory, "settings.json");

    /// <summary>The currently loaded settings instance (mutated in place and saved as a whole).</summary>
    public AppSettings Current { get; private set; } = new();

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                Current = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions) ?? new AppSettings();
            }
        }
        catch (Exception)
        {
            // Corrupt settings must never prevent the app from starting.
            Current = new AppSettings();
        }

        return Current;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(Current, SerializerOptions));
        }
        catch (IOException)
        {
            // Best effort: never crash the app because settings could not be written.
        }
    }

    /// <summary>Applies a mutation to the current settings and persists them immediately.</summary>
    public void Update(Action<AppSettings> mutate)
    {
        mutate(Current);
        Save();
    }
}
