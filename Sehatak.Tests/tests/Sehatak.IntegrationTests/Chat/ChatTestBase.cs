using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Sehatak.API.Hubs;
using Sehatak.Domain.Entities.General;
using Sehatak.IntegrationTests.Appointments;
using Sehatak.IntegrationTests.Infrastructure;
using Shouldly;

namespace Sehatak.IntegrationTests.Chat;

/// <summary>What a fake hub client received: who (SignalR user id), which event, and its payload.</summary>
public record Sent(string To, string Method, object? Payload);

/// <summary>A hand-written IClientProxy: configuring NSubstitute substitutes inside Returns()/callbacks makes NSubstitute lose track of the call.</summary>
public sealed class RecordingProxy(Action<string, object?> record) : IClientProxy
{
    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        record(method, args.FirstOrDefault());
        return Task.CompletedTask;
    }
}

public abstract class ChatTestBase(SqlServerFixture fx) : AppointmentTestBase(fx)
{
    protected int A { get; private set; }          // receptionist
    protected int B { get; private set; }          // another receptionist
    protected int DocUser => Doc.UserId;           // doctor

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        A = await C.AddReceptionistAsync();
        B = await C.AddReceptionistAsync();
    }

    protected sealed class FakeHub
    {
        public ChatHubs Hub = null!;
        public IHubCallerClients Clients = null!;
        public IGroupManager Groups = null!;
        public readonly List<Sent> Sent = new();
        public readonly List<string> OthersEvents = new();
        public readonly List<(string Group, string Method)> GroupEvents = new();
    }

    /// <summary>A hub as if <paramref name="userId"/> were connected to <paramref name="centerId"/> (defaults to the test center).</summary>
    protected FakeHub NewHub(int userId, OnlineUserTracker? tracker = null, int? centerId = null, bool centerClaim = true, string connectionId = "conn-1")
    {
        var f = new FakeHub();
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (centerClaim) claims.Add(new Claim("CenterId", (centerId ?? C.Id).ToString()));

        var ctx = Substitute.For<HubCallerContext>();
        ctx.UserIdentifier.Returns(userId.ToString());
        ctx.User.Returns(new ClaimsPrincipal(new ClaimsIdentity(claims, "test")));
        ctx.ConnectionId.Returns(connectionId);

        static IClientProxy Proxy(Action<string, object?> record) => new RecordingProxy(record);

        var clients = Substitute.For<IHubCallerClients>();
        clients.User(Arg.Any<string>()).Returns(ci =>
        {
            var to = ci.ArgAt<string>(0);
            return Proxy((m, payload) => f.Sent.Add(new Sent(to, m, payload)));
        });
        var othersProxy = Proxy((m, _) => f.OthersEvents.Add(m));
        clients.Others.Returns(othersProxy);
        clients.OthersInGroup(Arg.Any<string>()).Returns(ci =>
        {
            var g = ci.ArgAt<string>(0);
            return Proxy((m, _) => f.GroupEvents.Add((g, m)));
        });

        f.Clients = clients;
        f.Groups = Substitute.For<IGroupManager>();
        f.Hub = new ChatHubs(Fx.Harness.TenantFactory, tracker ?? new OnlineUserTracker()) { Context = ctx, Clients = clients, Groups = f.Groups };
        return f;
    }

    /// <summary>The SignalR address of a user: the bare id (old) or "center:id" (fixed). Passing both ways keeps the non-bug tests green before and after the fix.</summary>
    protected bool IsTo(Sent s, int userId) => s.To == userId.ToString() || s.To == $"{C.Id}:{userId}";

    protected static int IdOf(object? payload) =>
        (int)payload!.GetType().GetProperty("id")!.GetValue(payload)!;

    protected static int MessageIdOf(object? payload) =>
        (int)payload!.GetType().GetProperty("MessageId")!.GetValue(payload)!;

    protected async Task<int> AddChatAsync(int sender, int receiver, string text, int minutesAgo = 0, bool read = false, bool deleted = false)
    {
        await using var db = Db();
        var m = new Sehatak.Domain.Entities.General.Chat
        {
            SenderId = sender,
            ReceiverId = receiver,
            Message = text,
            SentAt = DateTime.UtcNow.AddMinutes(-minutesAgo),
            IsRead = read,
            IsDeleted = deleted
        };
        db.Chats.Add(m);
        await db.SaveChangesAsync();
        return m.Id;
    }

    protected async Task<Sehatak.Domain.Entities.General.Chat> ChatAsync(int id)
    {
        await using var db = Db();
        return await db.Chats.AsNoTracking().SingleAsync(c => c.Id == id);
    }

    protected async Task<int> ChatCountAsync()
    {
        await using var db = Db();
        return await db.Chats.CountAsync();
    }
}