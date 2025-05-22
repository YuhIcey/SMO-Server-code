using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace SMOxServer;

// Network packet types (matching C++ enum)
public enum PacketType : byte
{
    // TCP Packets (Reliable)
    Connect = 0,
    Disconnect = 1,
    InventoryUpdate = 2,
    QuestUpdate = 3,
    ChatMessage = 4,
    
    // UDP Packets (Real-time)
    PlayerUpdate = 10,
    WorldState = 11,
    CombatState = 12,
    Damage = 13,
    SurvivalStats = 14,
    AnimationState = 15
}

// Network packet structure (matching C++ struct)
public class NetworkPacket
{
    public PacketType Type { get; set; }
    public uint Size { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
}

// Player state structure (matching C++ struct)
public class NetworkPlayerState
{
    public string PlayerId { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float Pitch { get; set; }
    public float Yaw { get; set; }
    public float Roll { get; set; }
    public float Health { get; set; }
    public bool IsDead { get; set; }
    public bool IsInWater { get; set; }
    public uint SequenceNumber { get; set; }  // For UDP packet ordering
}

// World state structure (matching C++ struct)
public class NetworkWorldState
{
    public float GameTime { get; set; }
    public int WeatherID { get; set; }
    public string CellID { get; set; } = string.Empty;
    public uint SequenceNumber { get; set; }  // For UDP packet ordering
}

// Player session information
public class PlayerSession
{
    public string PlayerId { get; set; } = string.Empty;
    public NetworkPlayerState State { get; set; } = new();
    public bool IsConnected { get; set; }
    public DateTime LastUpdate { get; set; }
    public TcpClient TcpClient { get; set; } = null!;
    public IPEndPoint UdpEndPoint { get; set; } = null!;
    public uint LastProcessedSequence { get; set; }
}

class Program
{
    private static TcpListener? _tcpServer;
    private static UdpClient? _udpServer;
    private static readonly Dictionary<string, PlayerSession> _playerSessions = new();
    private static readonly object _sessionLock = new();
    private static NetworkWorldState _worldState = new();
    private static bool _isRunning;
    private static uint _sequenceCounter;
    private static ServerConfig _config = null!;

    static async Task Main(string[] args)
    {
        Logger.PrintBanner();
        AdminManager.Load(); // Load admins and bans
        _config = ServerConfig.Load();
        Logger.PrintHeaderBox(_config);
        Logger.HR();
        // Start admin command loop
        _ = Task.Run(AdminCommandLoop);
        
        _tcpServer = new TcpListener(IPAddress.Parse(_config.IP), ServerConfig.TCPPort);
        _udpServer = new UdpClient(ServerConfig.UDPPort);
        
        try
        {
            _tcpServer.Start();
            _isRunning = true;
            Logger.Success($"TCP Server started on {_config.IP}:{ServerConfig.TCPPort}");
            Logger.Success($"UDP Server started on {_config.IP}:{ServerConfig.UDPPort}");
            Logger.HR();

            // Start session cleanup task
            _ = Task.Run(CleanupInactiveSessions);
            
            // Start UDP receive loop
            _ = Task.Run(ReceiveUdpPacketsAsync);

            // Main TCP server loop
            while (_isRunning)
            {
                try
                {
                    var client = await _tcpServer.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleTcpClientAsync(client));
                }
                catch (Exception ex)
                {
                    Logger.Error($"Error accepting TCP client: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Server error: {ex.Message}");
        }
        finally
        {
            _tcpServer?.Stop();
            _udpServer?.Close();
        }
    }

    private static async Task HandleTcpClientAsync(TcpClient client)
    {
        var stream = client.GetStream();
        var buffer = new byte[4096];
        var playerId = string.Empty;
        string steamId = string.Empty;
        try
        {
            while (client.Connected)
            {
                // Read packet type
                if (await stream.ReadAsync(buffer, 0, 1) != 1) break;
                var packetType = (PacketType)buffer[0];
                // Read packet size
                if (await stream.ReadAsync(buffer, 0, 4) != 4) break;
                var packetSize = BitConverter.ToUInt32(buffer, 0);
                // Read packet data
                var data = new byte[packetSize];
                if (await stream.ReadAsync(data, 0, (int)packetSize) != packetSize) break;
                // On first packet, extract Steam ID and check ban
                if (string.IsNullOrEmpty(steamId))
                {
                    // Assume JSON with PlayerId as Steam ID
                    var json = System.Text.Encoding.UTF8.GetString(data);
                    var state = System.Text.Json.JsonSerializer.Deserialize<NetworkPlayerState>(json);
                    steamId = state?.PlayerId ?? "";
                    if (AdminManager.IsBanned(steamId))
                    {
                        Logger.Warning($"Rejected banned player {steamId}");
                        await SendTcpPacketAsync(client, PacketType.Disconnect, System.Text.Encoding.UTF8.GetBytes("You are banned from this server."));
                        break;
                    }
                }
                // Process packet
                await ProcessTcpPacketAsync(packetType, data, client);
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Error handling TCP client: {ex.Message}");
        }
        finally
        {
            if (!string.IsNullOrEmpty(playerId))
            {
                RemovePlayer(playerId);
            }
            client.Close();
        }
    }

    private static async Task ReceiveUdpPacketsAsync()
    {
        while (_isRunning)
        {
            try
            {
                var result = await _udpServer!.ReceiveAsync();
                var data = result.Buffer;
                
                if (data.Length < 5) continue; // Minimum packet size (1 byte type + 4 bytes size)
                
                var packetType = (PacketType)data[0];
                var packetSize = BitConverter.ToUInt32(data, 1);
                
                if (data.Length < packetSize + 5) continue; // Invalid packet size
                
                var packetData = new byte[packetSize];
                Array.Copy(data, 5, packetData, 0, packetSize);
                
                await ProcessUdpPacketAsync(packetType, packetData, result.RemoteEndPoint);
            }
            catch (Exception ex)
            {
                Logger.Error($"Error receiving UDP packet: {ex.Message}");
            }
        }
    }

    private static async Task ProcessTcpPacketAsync(PacketType type, byte[] data, TcpClient client)
    {
        switch (type)
        {
            case PacketType.Connect:
                var connectData = JsonSerializer.Deserialize<NetworkPlayerState>(data);
                if (connectData != null)
                {
                    // Check if server is full
                    if (_playerSessions.Count >= _config.MaxPlayers)
                    {
                        Logger.Warning($"Connection rejected: Server is full ({_playerSessions.Count}/{_config.MaxPlayers})");
                        client.Close();
                        return;
                    }

                    // Check password if required
                    if (_config.RequirePassword)
                    {
                        var password = Encoding.UTF8.GetString(data);
                        if (password != _config.Password)
                        {
                            Logger.Warning($"Connection rejected: Invalid password from {connectData.PlayerId}");
                            client.Close();
                            return;
                        }
                    }

                    AddPlayer(connectData.PlayerId, client);
                    await BroadcastPlayerStateAsync(connectData);
                }
                break;

            case PacketType.Disconnect:
                var disconnectData = JsonSerializer.Deserialize<NetworkPlayerState>(data);
                if (disconnectData != null)
                {
                    RemovePlayer(disconnectData.PlayerId);
                    await BroadcastPlayerStateAsync(disconnectData);
                }
                break;

            case PacketType.InventoryUpdate:
            case PacketType.QuestUpdate:
            case PacketType.ChatMessage:
                // Forward these reliable packets to all other clients
                await BroadcastTcpPacketAsync(type, data);
                break;
        }
    }

    private static async Task ProcessUdpPacketAsync(PacketType type, byte[] data, IPEndPoint remoteEndPoint)
    {
        switch (type)
        {
            case PacketType.PlayerUpdate:
                var playerData = JsonSerializer.Deserialize<NetworkPlayerState>(data);
                if (playerData != null)
                {
                    // Update UDP endpoint for this player
                    lock (_sessionLock)
                    {
                        if (_playerSessions.TryGetValue(playerData.PlayerId, out var session))
                        {
                            session.UdpEndPoint = remoteEndPoint;
                            session.State = playerData;
                            session.LastUpdate = DateTime.UtcNow;
                        }
                    }
                    await BroadcastUdpPacketAsync(type, data);
                }
                break;

            case PacketType.WorldState:
                var worldData = JsonSerializer.Deserialize<NetworkWorldState>(data);
                if (worldData != null)
                {
                    _worldState = worldData;
                    await BroadcastUdpPacketAsync(type, data);
                }
                break;

            case PacketType.CombatState:
            case PacketType.Damage:
            case PacketType.SurvivalStats:
            case PacketType.AnimationState:
                await BroadcastUdpPacketAsync(type, data);
                break;
        }
    }

    private static void AddPlayer(string playerId, TcpClient client)
    {
        lock (_sessionLock)
        {
            _playerSessions[playerId] = new PlayerSession
            {
                PlayerId = playerId,
                TcpClient = client,
                IsConnected = true,
                LastUpdate = DateTime.UtcNow
            };
        }
        Logger.Success($"Player connected: {playerId} ({_playerSessions.Count}/{_config.MaxPlayers})");
    }

    private static void RemovePlayer(string playerId)
    {
        lock (_sessionLock)
        {
            if (_playerSessions.Remove(playerId))
            {
                Logger.Info($"Player disconnected: {playerId} ({_playerSessions.Count}/{_config.MaxPlayers})");
            }
        }
    }

    private static async Task BroadcastTcpPacketAsync(PacketType type, byte[] data)
    {
        var tasks = new List<Task>();
        lock (_sessionLock)
        {
            foreach (var session in _playerSessions.Values)
            {
                if (session.IsConnected)
                {
                    tasks.Add(SendTcpPacketAsync(session.TcpClient, type, data));
                }
            }
        }
        await Task.WhenAll(tasks);
    }

    private static async Task BroadcastUdpPacketAsync(PacketType type, byte[] data)
    {
        var tasks = new List<Task>();
        lock (_sessionLock)
        {
            foreach (var session in _playerSessions.Values)
            {
                if (session.IsConnected && session.UdpEndPoint != null)
                {
                    tasks.Add(SendUdpPacketAsync(session.UdpEndPoint, type, data));
                }
            }
        }
        await Task.WhenAll(tasks);
    }

    private static async Task SendTcpPacketAsync(TcpClient client, PacketType type, byte[] data)
    {
        try
        {
            var stream = client.GetStream();
            await stream.WriteAsync(new[] { (byte)type });
            await stream.WriteAsync(BitConverter.GetBytes((uint)data.Length));
            await stream.WriteAsync(data);
        }
        catch (Exception ex)
        {
            Logger.Error($"Error sending TCP packet: {ex.Message}");
        }
    }

    private static async Task SendUdpPacketAsync(IPEndPoint endPoint, PacketType type, byte[] data)
    {
        try
        {
            var packet = new byte[data.Length + 5];
            packet[0] = (byte)type;
            BitConverter.GetBytes((uint)data.Length).CopyTo(packet, 1);
            data.CopyTo(packet, 5);
            
            await _udpServer!.SendAsync(packet, packet.Length, endPoint);
        }
        catch (Exception ex)
        {
            Logger.Error($"Error sending UDP packet: {ex.Message}");
        }
    }

    private static async Task BroadcastPlayerStateAsync(NetworkPlayerState state)
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(state);
        await BroadcastUdpPacketAsync(PacketType.PlayerUpdate, data);
    }

    private static async Task BroadcastWorldStateAsync()
    {
        var data = JsonSerializer.SerializeToUtf8Bytes(_worldState);
        await BroadcastUdpPacketAsync(PacketType.WorldState, data);
    }

    private static async Task CleanupInactiveSessions()
    {
        while (_isRunning)
        {
            await Task.Delay(TimeSpan.FromSeconds(30));

            var now = DateTime.UtcNow;
            var inactivePlayers = new List<string>();

            lock (_sessionLock)
            {
                foreach (var session in _playerSessions.Values)
                {
                    if (now - session.LastUpdate > TimeSpan.FromMinutes(1))
                    {
                        inactivePlayers.Add(session.PlayerId);
                    }
                }
            }

            foreach (var playerId in inactivePlayers)
            {
                RemovePlayer(playerId);
            }
        }
    }

    // Admin command loop for server console
    private static void AdminCommandLoop()
    {
        while (true)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write(Logger.GetTerminalPrompt());
            Console.ResetColor();
            
            var input = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(input)) continue;
            
            var args = input.Trim().Split(' ', 2);
            var cmd = args[0].ToLower();
            var arg = args.Length > 1 ? args[1] : string.Empty;
            
            switch (cmd)
            {
                case "kick":
                    if (string.IsNullOrEmpty(arg))
                    {
                        Logger.PrintError("Usage: kick <steamId>");
                        break;
                    }
                    KickPlayer(arg);
                    break;
                    
                case "ban":
                    if (string.IsNullOrEmpty(arg))
                    {
                        Logger.PrintError("Usage: ban <steamId>");
                        break;
                    }
                    BanPlayer(arg);
                    break;
                    
                case "unban":
                    if (string.IsNullOrEmpty(arg))
                    {
                        Logger.PrintError("Usage: unban <steamId>");
                        break;
                    }
                    AdminManager.Unban(arg);
                    Logger.PrintCommandResponse($"UNBANNED STEAM ID: {arg}");
                    break;
                    
                case "admins":
                    var admins = AdminManager.GetAdmins().ToList();
                    if (admins.Count == 0)
                    {
                        Logger.PrintCommandResponse("NO ADMINISTRATORS CONFIGURED\nAdd Steam IDs to admin.cfg");
                    }
                    else
                    {
                        Logger.PrintCommandResponse("ADMINISTRATOR LIST:\n" + string.Join("\n", admins.Select(a => $"STEAM ID: {a}")));
                    }
                    break;
                    
                case "bans":
                    var bans = AdminManager.GetBans().ToList();
                    if (bans.Count == 0)
                    {
                        Logger.PrintCommandResponse("NO ACTIVE BANS");
                    }
                    else
                    {
                        Logger.PrintCommandResponse("BANNED USERS:\n" + string.Join("\n", bans.Select(b => $"STEAM ID: {b}")));
                    }
                    break;
                    
                case "help":
                    Logger.PrintCommandResponse(
                        "AVAILABLE COMMANDS:\n" +
                        "  kick <steamId>  - Disconnect user from server\n" +
                        "  ban <steamId>   - Ban user from server\n" +
                        "  unban <steamId> - Remove user from ban list\n" +
                        "  admins         - List administrator Steam IDs\n" +
                        "  bans           - List banned Steam IDs\n" +
                        "  help           - Show this help message\n" +
                        "  clear          - Clear terminal screen\n" +
                        "  exit           - Shutdown server"
                    );
                    break;
                    
                case "clear":
                    Console.Clear();
                    Logger.PrintBanner();
                    Logger.PrintHeaderBox(_config);
                    break;
                    
                case "exit":
                    Logger.PrintCommandResponse("INITIATING SHUTDOWN SEQUENCE...");
                    Thread.Sleep(1000);
                    Environment.Exit(0);
                    break;
                    
                default:
                    Logger.PrintError($"Unknown command: {cmd}\nType 'help' for available commands");
                    break;
            }
        }
    }

    // Helper: Kick player by Steam ID
    private static void KickPlayer(string steamId)
    {
        lock (_sessionLock)
        {
            var session = _playerSessions.Values.FirstOrDefault(s => s.State.PlayerId == steamId);
            if (session != null)
            {
                Logger.PrintCommandResponse($"DISCONNECTING USER: {steamId}");
                SendTcpPacketAsync(session.TcpClient, PacketType.Disconnect, System.Text.Encoding.UTF8.GetBytes("You have been kicked by an administrator.")).Wait();
                session.TcpClient.Close();
                RemovePlayer(steamId);
                Logger.PrintCommandResponse("USER DISCONNECTED");
            }
            else
            {
                Logger.PrintError($"User not found: {steamId}");
            }
        }
    }

    // Helper: Ban player by Steam ID
    private static void BanPlayer(string steamId)
    {
        lock (_sessionLock)
        {
            var session = _playerSessions.Values.FirstOrDefault(s => s.State.PlayerId == steamId);
            AdminManager.Ban(steamId);
            Logger.PrintCommandResponse($"BANNED USER: {steamId}");
            if (session != null)
            {
                SendTcpPacketAsync(session.TcpClient, PacketType.Disconnect, System.Text.Encoding.UTF8.GetBytes("You have been banned by an administrator.")).Wait();
                session.TcpClient.Close();
                RemovePlayer(steamId);
                Logger.PrintCommandResponse("USER DISCONNECTED");
            }
        }
    }
} 