using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Fetchgoods.Text.Json.Extensions;
using GiganticEmu.Shared.Backend;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

public class MiceClient
{
    public event EventHandler? ConnectionClosed;
    public ILogger<MiceClient> Logger { get; init; }
    public Guid UserId { get; private set; } = Guid.Empty;
    public int MotigaId { get; private set; } = 0;
    const int MIN_BUFFER_SIZE = 512;
    const int MAX_LENGTH_BYTES = sizeof(long) + 1;
    private TcpClient _tcp = default!;
    private NetworkStream _tcpStream = default!;
    private Pipe _pipe = new Pipe();
    private bool _authenticated = false;
    private bool _closed = false;
    private CancellationTokenSource _cts = default!;
    private Salsa _salsaIn = default!;
    private Salsa _salsaOut = default!;
    private MiceCommandHandler _commandHandler;
    private BackendConfiguration _configuration;
    private IDbContextFactory<ApplicationDatabase> _databaseFactory;

    public MiceClient(ILogger<MiceClient> logger, MiceCommandHandler commandHandler, IOptions<BackendConfiguration> configuration, IDbContextFactory<ApplicationDatabase> database)
    {
        Logger = logger;
        _commandHandler = commandHandler;
        _configuration = configuration.Value;
        _databaseFactory = database;
    }

    public async Task Run(TcpClient tcp, CancellationToken cancellationToken = default)
    {
        _tcp = tcp;
        _tcpStream = _tcp.GetStream();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            Task receiving = ReceiveTask();
            Task reading = ReadTask();
            Task updateParty = UpdatePartyTask();

            await Task.WhenAll(reading, receiving, updateParty);
            _tcp.Close();
        }
        finally
        {
            using var db = _databaseFactory.CreateDbContext();

            var user = await db.Users.SingleAsync(user => user.Id == UserId);
            user.ClearSession();
            db.GroupInvites.RemoveRange(await db.GroupInvites
                .Where(invite => invite.UserId == user.Id || invite.InvitedUserId == user.Id)
                .ToListAsync()
            );
            await db.SaveChangesAsync();

            if (ConnectionClosed is EventHandler handler)
            {
                handler(this, new EventArgs());
            }
        }
    }

    private async Task ReceiveTask()
    {
        var writer = _pipe.Writer;
        while (!_closed)
        {
            Memory<byte> memory = writer.GetMemory(MIN_BUFFER_SIZE);
            try
            {
                int bytesRead = await _tcpStream.ReadAsync(memory, _cts.Token);
                Logger.LogDebug("Receiving...");
                if (bytesRead == 0)
                {
                    break;
                }
                // Tell the PipeWriter how much was read from the Socket
                writer.Advance(bytesRead);
            }
            catch (OperationCanceledException)
            {
                // Client requested disconnected
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Exception during receive.");
                break;
            }

            // Make the data available to the PipeReader
            FlushResult result = await writer.FlushAsync();

            if (result.IsCompleted)
            {
                break;
            }
        }

        _cts.Cancel();

        // Tell the PipeReader that there's no more data coming
        writer.Complete();
    }

    private async Task ReadTask()
    {
        var pipeReader = _pipe.Reader;
        int cmdLen = 0;
        bool cmdReady = false;
        while (!_closed)
        {
            ReadResult result = await pipeReader.ReadAsync(_cts.Token);
            var buffer = result.Buffer;

            try
            {
                if (!cmdReady)
                {
                    cmdLen <<= 7;
                    var next = buffer.Slice(buffer.Start, 1).First.Span[0];
                    cmdLen += next & 0x7F;
                    if (next < 0x80)
                    {
                        cmdReady = true;
                    }

                    pipeReader.AdvanceTo(buffer.GetPosition(1, buffer.Start));
                }
                else if (buffer.Length >= cmdLen)
                {
                    Logger.LogDebug("Received command (len: {lentgh})", cmdLen);
                    await HandleCommand(buffer.Slice(buffer.Start, cmdLen).ToArray());
                    pipeReader.AdvanceTo(buffer.GetPosition(cmdLen));
                    cmdLen = 0;
                    cmdReady = false;
                }
                else
                {
                    pipeReader.AdvanceTo(buffer.Start, buffer.End);
                    Logger.LogDebug("Waiting for more data!");
                }
            }
            catch (OperationCanceledException)
            {
                // Client requested disconnected
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Exception during read.");
                break;
            }

            // Stop reading if there's no more data coming
            if (result.IsCompleted)
            {
                break;
            }
        }

        _cts.Cancel();

        // Mark the PipeReader as complete
        _tcpStream.Dispose();
    }

    private async Task UpdatePartyTask()
    {
        try
        {
            var lastSessionVersion = 0;
            while (true)
            {
                if (_authenticated)
                {
                    using var db = _databaseFactory.CreateDbContext();

                    var user = await db.Users.SingleAsync(user => user.Id == UserId);
                    var sessionHost = await db.Users.SingleAsync(x => x.SessionId == user.SessionId && x.IsSessionHost);

                    if (sessionHost.SessionVersion != lastSessionVersion)
                    {
                        lastSessionVersion = sessionHost.SessionVersion;

                        var members = await db.Users
                            .Where(x => x.SessionId == user.SessionId)
                            .ToDictionaryAsync(x => x.MotigaId, x => new
                            {
                                username = x.UserName,
                                member_settings = x.MemberSettings.FromJsonTo<dynamic>()
                            });

                        await SendMessage(new object[] { "party.stateupdated", new
                        {
                            data = new {
                                changed_keys = new
                                {
                                    document_version = lastSessionVersion + 100,
                                }
                            }
                        }});
                    }


                    //await SendMessage(new object[] { "lobby.notifyjoin", new object { } });
                }
                await Task.Delay(TimeSpan.FromMilliseconds(1000), _cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Client requested disconnected
        }
    }

    public async Task SendMessage(object response)
    {
        var json = response.ToJson();
        Logger.LogDebug(response.ToJson());
        var encrypted = _salsaOut.Encrypt(json);
        var length = EncodeSize(encrypted.Length);
        var packet = new byte[length.Length + encrypted.Length];
        System.Buffer.BlockCopy(length, 0, packet, 0, length.Length);
        System.Buffer.BlockCopy(encrypted, 0, packet, length.Length, encrypted.Length);
        await _tcpStream.WriteAsync(packet);
    }

    private byte[] EncodeSize(int size)
    {
        int length = size > 0 ? (int)Math.Floor(Math.Log(size) / Math.Log(0x80)) + 1 : 1;
        var data = new byte[length];

        if (length == 1)
            data[0] = (byte)size;
        else
        {
            int i = length - 2;
            data[length - 1] = (byte)(size & 0x7F);

            for (; i >= 0; i--)
            {
                size >>= 7;
                data[i] = (byte)((size & 0x7F) | 0x80);
            }
        }

        return data;
    }

    private async Task HandleCommand(byte[] data)
    {
        if (!_authenticated)
        {
            await Authenticate(data);
            return;
        }

        var content = _salsaIn.Decrypt(data);

        dynamic msg;
        try
        {
            msg = content.FromJsonTo<dynamic>();
        }
        catch (System.Exception ex)
        {
            Logger.LogError(ex, "Exception parsing json message.");
            throw;
        }

        var (cmd, payload, id) = (msg[0], msg[1], msg[2]);

        if (cmd == ".close" || cmd == "party.leave")
        {
            Logger.LogInformation("Received .close command!");
            _closed = true;
            return;
        }

        if (_commandHandler.CanHandle(cmd))
        {
            Logger.LogInformation("Received handled command: {cmd}", cmd as string);
            // _logger.LogDebug("{msg}", ((object)payload).ToJson());
            try
            {
                var response = await _commandHandler.Handle(cmd, payload, this);
                if (response != null)
                {
                    var responseMsg = new[] { new[] { response }, id };
                    await SendMessage(responseMsg);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Exception while handling command {cmd}.", cmd as string);
            }
        }
        else
        {
            Logger.LogInformation("Received unhandled command: {cmd}", cmd as string);
            //_logger.LogDebug("{msg}", ((object)payload).ToJson());
        }
    }

    public ApplicationDatabase CreateDbContext()
    {
        return _databaseFactory.CreateDbContext();
    }

    private async Task Authenticate(byte[] data)
    {
        var content = new Salsa(_configuration.SalsaCK, 12).Decrypt(data);

        string token;
        try
        {
            var msg = content.FromJsonTo<dynamic>();
            token = msg[0];
        }
        catch (System.Exception ex)
        {
            Logger.LogError(ex, "Exception parsing json message.");
            throw;
        }

        using var database = CreateDbContext();

        var user = await database.Users
            .Where(user => user.AuthToken == token)
            .FirstOrDefaultAsync();

        if (user != null && user.SalsaSCK is string salsaSCK)
        {
            UserId = user.Id;
            MotigaId = user.MotigaId;

            _salsaIn = new Salsa(salsaSCK, 16);
            _salsaOut = new Salsa(salsaSCK, 16);

            await SendMessage(new object[]
            {
                ".auth",
                new {
                    time = 1,
                    moid = user.MotigaId,
                    exp = 0,
                    rank = 1,
                    name = user.UserName,
                    deviceid = "noString",
                    gameid = "ggc",
                    version = "301530",
                    xmpp = new
                    {
                        host = "127.0.0.1",
                    },
                },
            });

            user.ClearSession();
            await database.SaveChangesAsync();

            _authenticated = true;

            Logger.LogInformation("Client authenticated using salsa sck: {sck}!", salsaSCK);
        }
        else
        {
            await SendMessage(new object[] { ".auth", false });
        }
    }
}