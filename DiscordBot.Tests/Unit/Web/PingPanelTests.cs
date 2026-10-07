using System.Net;
using System.Text;
using Bunit;
using DiscordBot.Contracts;
using DiscordBot.Web.Components;
using DiscordBot.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordBot.Tests.Unit.Web;

public class PingPanelTests : BunitContext
{
    private sealed class Stub : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _respond;

        public Stub(Func<HttpRequestMessage, string, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public List<(string Path, string Body)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((request.RequestUri!.AbsolutePath, body));

            return _respond(request, body);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static readonly List<RoleDto> Roles =
    [
        new() { Id = "11", Name = "Raiders", Position = 2 },
        new() { Id = "12", Name = "Officers", Position = 1 }
    ];

    private string? _sentMessage;

    private (IRenderedComponent<PingPanel> Panel, Stub Api) RenderPanel(Func<HttpRequestMessage, string, HttpResponseMessage> api, bool showRoster = true)
    {
        var stub = new Stub(api);
        Services.AddSingleton(new ApiService(new HttpClient(stub) { BaseAddress = new Uri("http://api.test") }));

        var panel = Render<PingPanel>(parameters => parameters
            .Add(p => p.EventId, 99UL)
            .Add(p => p.Roles, Roles)
            .Add(p => p.EventRoleIds, new ulong[] { 11 })
            .Add(p => p.ShowRoster, showRoster)
            .Add(p => p.OnSent, EventCallback.Factory.Create<string>(this, message => _sentMessage = message)));

        return (panel, stub);
    }

    private static string PreviewJson(string recipients, string? recent = null) =>
        $"{{\"recipients\":[{recipients}],\"recentlyPingedAt\":{(recent == null ? "null" : $"\"{recent}\"")}}}";

    private static HttpResponseMessage Standard(HttpRequestMessage request, string body) => request.RequestUri!.AbsolutePath.EndsWith("/preview")
        ? Json(PreviewJson("{\"userId\":\"1\",\"displayName\":\"Valhera\",\"group\":1},{\"userId\":\"2\",\"displayName\":null,\"group\":2}"))
        : Json("{\"count\":2}");

    [Fact]
    public void Roster_ChosenAndConfirmed_ShowsWhoWillBePingedAndPostsThePingWithTheNote()
    {
        var (panel, api) = RenderPanel(Standard);

        panel.Find("#ping-roster").Click();
        panel.WaitForElement("#ping-count");

        Assert.Contains("This will ping 2 people", panel.Find("#ping-count").TextContent);
        Assert.Equal(new[] { "Valhera · G1", "User 2 · G2" }, panel.FindAll(".ping-recipient").Select(e => e.TextContent.Trim()));
        panel.Find("#ping-note").Input("Bring flasks");
        panel.Find("#ping-send").Click();

        panel.WaitForAssertion(() => Assert.Equal("Pinged 2 people in the event's channel.", _sentMessage));
        var sent = api.Requests.Last();
        Assert.Equal("/api/events/99/pings", sent.Path);
        Assert.Contains("\"target\":\"Roster\"", sent.Body);
        Assert.Contains("\"note\":\"Bring flasks\"", sent.Body);
        Assert.Empty(panel.FindAll("#ping-send"));
    }

    [Fact]
    public void NoResponse_Chosen_StartsWithTheEventsRolesTickedAndSendsThem()
    {
        var (panel, api) = RenderPanel(Standard);

        panel.Find("#ping-noresponse").Click();
        panel.WaitForElement("#ping-count");

        Assert.True(panel.Find("#ping-role-11").HasAttribute("checked"));
        Assert.False(panel.Find("#ping-role-12").HasAttribute("checked"));
        Assert.Contains("\"roleIds\":[11]", api.Requests.Last().Body);
    }

    [Fact]
    public void NoResponse_RoleAndUnsureChanged_LooksTheRecipientsUpAgainWithTheNewChoices()
    {
        var (panel, api) = RenderPanel(Standard);
        panel.Find("#ping-noresponse").Click();
        panel.WaitForElement("#ping-count");

        panel.Find("#ping-role-12").Change(true);
        panel.Find("#ping-include-unsure").Change(true);

        panel.WaitForAssertion(() =>
        {
            var last = api.Requests.Last();
            Assert.EndsWith("/preview", last.Path);
            Assert.Contains("\"roleIds\":[11,12]", last.Body);
            Assert.Contains("\"includeUnsure\":true", last.Body);
        });
    }

    [Fact]
    public void Preview_ApiExplainsWhyNobodyCanBePinged_ShowsItInsteadOfASendButton()
    {
        var (panel, _) = RenderPanel((request, body) => Json("{\"title\":\"Bad Request\",\"status\":400,\"detail\":\"Choose at least one role, so the bot knows whose answers to look for.\"}", HttpStatusCode.BadRequest));

        panel.Find("#ping-noresponse").Click();

        panel.WaitForElement("#ping-problem");
        Assert.Contains("Choose at least one role", panel.Find("#ping-problem").TextContent);
        Assert.Empty(panel.FindAll("#ping-send"));
    }

    [Fact]
    public void Preview_SamePeoplePingedAMomentAgo_WarnsBeforeSending()
    {
        var recent = DateTime.UtcNow.AddMinutes(-3).ToString("O");
        var (panel, _) = RenderPanel((request, body) => Json(PreviewJson("{\"userId\":\"1\",\"displayName\":\"Valhera\",\"group\":null}", recent)));

        panel.Find("#ping-standby").Click();

        panel.WaitForElement("#ping-recent");
        Assert.Contains("pinged 3 minutes ago", panel.Find("#ping-recent").TextContent);
        Assert.False(panel.Find("#ping-send").HasAttribute("disabled"));
    }

    [Fact]
    public void Preview_NobodyToPing_CannotBeSent()
    {
        var (panel, _) = RenderPanel((request, body) => Json(PreviewJson("")));

        panel.Find("#ping-standby").Click();

        panel.WaitForElement("#ping-count");
        Assert.True(panel.Find("#ping-send").HasAttribute("disabled"));
    }

    [Fact]
    public void Send_ApiRefusesThePing_ShowsTheReasonAndKeepsThePanelOpen()
    {
        var (panel, _) = RenderPanel((request, body) => request.RequestUri!.AbsolutePath.EndsWith("/preview")
            ? Json(PreviewJson("{\"userId\":\"1\",\"displayName\":\"Valhera\",\"group\":1}"))
            : Json("{\"title\":\"Bad Request\",\"status\":400,\"detail\":\"I couldn't post the ping.\"}", HttpStatusCode.BadRequest));
        panel.Find("#ping-roster").Click();
        panel.WaitForElement("#ping-send");

        panel.Find("#ping-send").Click();

        panel.WaitForElement("#ping-error");
        Assert.Contains("couldn't post the ping", panel.Find("#ping-error").TextContent);
        Assert.Null(_sentMessage);
        Assert.NotEmpty(panel.FindAll("#ping-send"));
    }

    [Fact]
    public void Cancel_ChosenPing_ClosesThePanelWithoutSendingAnything()
    {
        var (panel, api) = RenderPanel(Standard);
        panel.Find("#ping-roster").Click();
        panel.WaitForElement("#ping-cancel");

        panel.Find("#ping-cancel").Click();

        Assert.Empty(panel.FindAll(".ping-confirm"));
        Assert.DoesNotContain(api.Requests, r => !r.Path.EndsWith("/preview"));
    }

    [Fact]
    public void Render_EventWithoutRaidGroups_OffersOnlyThePingForPeopleWhoHaveNotResponded()
    {
        var (panel, _) = RenderPanel(Standard, showRoster: false);

        Assert.Empty(panel.FindAll("#ping-roster"));
        Assert.Empty(panel.FindAll("#ping-standby"));
        Assert.NotEmpty(panel.FindAll("#ping-noresponse"));
    }
}
