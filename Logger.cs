using System;
using System.Collections.Generic;

namespace SMOxServer;

public static class Logger
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Success
    }

    private static readonly object _lock = new();
    private static readonly Dictionary<LogLevel, (ConsoleColor Color, string Prefix)> _logStyles = new()
    {
        { LogLevel.Debug, (ConsoleColor.DarkGray, "DEBUG") },
        { LogLevel.Info, (ConsoleColor.Green, "INFO") },
        { LogLevel.Warning, (ConsoleColor.Yellow, "WARN") },
        { LogLevel.Error, (ConsoleColor.Red, "ERROR") },
        { LogLevel.Success, (ConsoleColor.Green, "OK") }
    };

    public static void Log(LogLevel level, string message)
    {
        lock (_lock)
        {
            var (color, prefix) = _logStyles[level];
            var originalColor = Console.ForegroundColor;
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.Write($"[{timestamp}]");
            Console.ForegroundColor = color;
            Console.Write($" [{prefix,-5}] ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(message);
            Console.ResetColor();
        }
    }

    public static void Debug(string message) => Log(LogLevel.Debug, message);
    public static void Info(string message) => Log(LogLevel.Info, message);
    public static void Warning(string message) => Log(LogLevel.Warning, message);
    public static void Error(string message) => Log(LogLevel.Error, message);
    public static void Success(string message) => Log(LogLevel.Success, message);

    public static void PrintBanner()
    {
        Console.Clear();
        Console.ForegroundColor = ConsoleColor.Green;
        
        // RobCo boot sequence
        string[] bootSequence = new[]
        {
            "ROBCO INDUSTRIES (TM) TERMLINK PROTOCOL",
            "ENTER PASSWORD NOW",
            "",
            "*** PASSWORD ACCEPTED ***",
            "",
            "INITIALIZING BOOT SEQUENCE...",
            "[==================================]",
            "",
            "ESTABLISHING NETWORK PROTOCOLS...",
            "TCP PORT: 2271 [OK]",
            "UDP PORT: 2272 [OK]",
            "",
            "LOADING ADMIN PROTOCOLS...",
            "[==================================]",
            "",
            "INITIALIZING SIERRA MADRE ONLINE...",
            ""
        };

        foreach (var line in bootSequence)
        {
            int pad = (Console.WindowWidth - line.Length) / 2;
            Console.WriteLine(new string(' ', Math.Max(0, pad)) + line);
            Thread.Sleep(150);
        }

        // SMO ASCII art logo
        string[] logo = new[]
        {
            "   SSSSSSSSSSSSSSS MMMMMMMM               MMMMMMMM     OOOOOOOOO                         ",
            " SS:::::::::::::::SM:::::::M             M:::::::M   OO:::::::::OO                       ",
            "S:::::SSSSSS::::::SM::::::::M           M::::::::M OO:::::::::::::OO                     ",
            "S:::::S     SSSSSSSM:::::::::M         M:::::::::MO:::::::OOO:::::::O                    ",
            "S:::::S            M::::::::::M       M::::::::::MO::::::O   O::::::Oxxxxxxx      xxxxxxx",
            "S:::::S            M:::::::::::M     M:::::::::::MO:::::O     O:::::O x:::::x    x:::::x ",
            " S::::SSSS         M:::::::M::::M   M::::M:::::::MO:::::O     O:::::O  x:::::x  x:::::x  ",
            "  SS::::::SSSSS    M::::::M M::::M M::::M M::::::MO:::::O     O:::::O   x:::::xx:::::x   ",
            "    SSS::::::::SS  M::::::M  M::::M::::M  M::::::MO:::::O     O:::::O    x::::::::::x    ",
            "       SSSSSS::::S M::::::M   M:::::::M   M::::::MO:::::O     O:::::O     x::::::::x     ",
            "            S:::::SM::::::M    M:::::M    M::::::MO:::::O     O:::::O     x::::::::x     ",
            "            S:::::SM::::::M     MMMMM     M::::::MO::::::O   O::::::O    x::::::::::x    ",
            "SSSSSSS     S:::::SM::::::M               M::::::MO:::::::OOO:::::::O   x:::::xx:::::x   ",
            "S::::::SSSSSS:::::SM::::::M               M::::::M OO:::::::::::::OO   x:::::x  x:::::x  ",
            "S:::::::::::::::SS M::::::M               M::::::M   OO:::::::::OO    x:::::x    x:::::x ",
            " SSSSSSSSSSSSSSS   MMMMMMMM               MMMMMMMM     OOOOOOOOO     xxxxxxx      xxxxxxx",
            "                                                                                         ",
            "                                                                                         "
        };

        foreach (var line in logo)
        {
            int pad = (Console.WindowWidth - line.Length) / 2;
            Console.WriteLine(new string(' ', Math.Max(0, pad)) + line);
            Thread.Sleep(20);
        }

        Thread.Sleep(500);
        Console.WriteLine();
    }

    public static void PrintHeaderBox(ServerConfig config)
    {
        string[] lines =
        {
            $"SERVER STATUS",
            $"",
            $"NAME: {config.ServerName}",
            $"MOTD: {config.MOTD}",
            $"MAX PLAYERS: {config.MaxPlayers}",
            $"PASSWORD PROTECTION: {(config.RequirePassword ? "ENABLED" : "DISABLED")}",
            $"TCP PORT: {ServerConfig.TCPPort} [LOCKED]",
            $"UDP PORT: {ServerConfig.UDPPort} [LOCKED]",
            $"",
            $"SERVER READY FOR CONNECTIONS"
        };

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n" + new string('=', Console.WindowWidth));
        
        foreach (var line in lines)
        {
            int pad = (Console.WindowWidth - line.Length) / 2;
            Console.WriteLine(new string(' ', Math.Max(0, pad)) + line);
            Thread.Sleep(50);
        }
        
        Console.WriteLine(new string('=', Console.WindowWidth));
        Console.WriteLine();
        Console.ResetColor();
    }

    public static void HR()
    {
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine(new string('-', Console.WindowWidth));
        Console.ResetColor();
    }

    public static string GetTerminalPrompt()
    {
        return $"\n> ";
    }

    public static void PrintCommandResponse(string response)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        foreach (var line in response.Split('\n'))
        {
            Console.WriteLine($"  {line.Trim()}");
            Thread.Sleep(30);
        }
        Console.ResetColor();
    }

    public static void PrintError(string error)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\nERROR: {error}");
        Console.ResetColor();
    }
} 