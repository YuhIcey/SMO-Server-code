using System.Text.Json;

namespace SMOxServer;

public class ServerConfig
{
    public string ServerName { get; set; } = "SMOx Server";
    public string MOTD { get; set; } = "Welcome to SMOx Server!";
    public string IP { get; set; } = "0.0.0.0";
    public string Password { get; set; } = "";
    public int MaxPlayers { get; set; } = 32;
    public bool RequirePassword { get; set; } = false;

    // Locked ports - cannot be changed
    public const int TCPPort = 2271;
    public const int UDPPort = 2272;

    public static ServerConfig Load()
    {
        try
        {
            if (File.Exists("server.cfg"))
            {
                var json = File.ReadAllText("server.cfg");
                var config = JsonSerializer.Deserialize<ServerConfig>(json);
                if (config != null) return config;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading config: {ex.Message}");
        }

        // Create default config if none exists
        var defaultConfig = new ServerConfig();
        defaultConfig.Save();
        return defaultConfig;
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText("server.cfg", json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving config: {ex.Message}");
        }
    }
} 