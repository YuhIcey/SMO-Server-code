using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SMOxServer;

public static class AdminManager
{
    private static readonly string AdminFile = "admin.cfg";
    private static readonly string BanFile = "bans.cfg";
    private static readonly ConcurrentDictionary<string, bool> Admins = new();
    private static readonly ConcurrentDictionary<string, bool> Bans = new();

    public static void Load()
    {
        // Ensure admin.cfg exists
        if (!File.Exists(AdminFile))
        {
            File.WriteAllLines(AdminFile, new[]
            {
                "# Sierra Madre Online Admins",
                "# Add one Steam ID per line"
            });
        }
        // Ensure bans.cfg exists
        if (!File.Exists(BanFile))
        {
            File.WriteAllLines(BanFile, new[]
            {
                "# Sierra Madre Online Bans",
                "# Add one Steam ID per line"
            });
        }

        Admins.Clear();
        Bans.Clear();
        foreach (var line in File.ReadAllLines(AdminFile))
        {
            var id = line.Trim();
            if (!string.IsNullOrEmpty(id) && !id.StartsWith("#"))
                Admins[id] = true;
        }
        foreach (var line in File.ReadAllLines(BanFile))
        {
            var id = line.Trim();
            if (!string.IsNullOrEmpty(id) && !id.StartsWith("#"))
                Bans[id] = true;
        }
    }

    public static void SaveAdmins()
    {
        File.WriteAllLines(AdminFile, Admins.Keys);
    }
    public static void SaveBans()
    {
        File.WriteAllLines(BanFile, Bans.Keys);
    }

    public static bool IsAdmin(string steamId) => Admins.ContainsKey(steamId);
    public static bool IsBanned(string steamId) => Bans.ContainsKey(steamId);

    public static void AddAdmin(string steamId)
    {
        Admins[steamId] = true;
        SaveAdmins();
    }
    public static void RemoveAdmin(string steamId)
    {
        Admins.TryRemove(steamId, out _);
        SaveAdmins();
    }
    public static void Ban(string steamId)
    {
        Bans[steamId] = true;
        SaveBans();
    }
    public static void Unban(string steamId)
    {
        Bans.TryRemove(steamId, out _);
        SaveBans();
    }
    public static IEnumerable<string> GetAdmins() => Admins.Keys;
    public static IEnumerable<string> GetBans() => Bans.Keys;
} 