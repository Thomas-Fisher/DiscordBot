using System.Net;
using System.Text;
using Bunit;
using DiscordBot.Web.Pages;
using DiscordBot.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace DiscordBot.Tests.Unit.Web;

/// <summary>The dashboard pages rendered for real, against a stubbed API, for the things only the page itself decides.</summary>
public class DashboardPageTests : BunitContext
{
    private sealed class StubApi : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

        public StubApi(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        public List<string> Paths { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(_respond(request));
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
    {
        return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static string Jwt()
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var exp = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
        return $"{B64("{\"alg\":\"none\"}")}.{B64($"{{\"exp\":{exp}}}")}.sig";
    }

    private static string SignedInBody() =>
        "{\"accessToken\":\"" + Jwt() + "\",\"expiresIn\":900,\"user\":{\"id\":\"1\",\"username\":\"Thomas\",\"guilds\":[]}}";

    private StubApi StartDashboard(Func<HttpRequestMessage, HttpResponseMessage> api, bool signedIn = true)
    {
        var stub = new StubApi(api);
        var tokens = new TokenService();
        var auth = new AuthService(new HttpClient(new StubApi(request => !signedIn
            ? Json("{}", HttpStatusCode.Unauthorized)
            : request.RequestUri!.AbsolutePath == "/api/auth/me" ? Json("{\"id\":\"1\",\"username\":\"Thomas\",\"guilds\":[]}") : Json(SignedInBody())))
        { BaseAddress = new Uri("http://api.test") }, tokens);
        var apiService = new ApiService(new HttpClient(stub) { BaseAddress = new Uri("http://api.test") });

        Services.AddSingleton(tokens);
        Services.AddSingleton(auth);
        Services.AddSingleton(apiService);
        Services.AddSingleton(new GuildClock(apiService));
        JSInterop.Mode = JSRuntimeMode.Loose;

        return stub;
    }

    [Fact]
    public void GuildEvents_SignedIn_ShowsEachEventsTimeInTheGuildsTimeZone()
    {
        // 19:00 UTC on 1 July is 20:00 in London (summer time
        StartDashboard(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/settings/guild/5" => Json("{\"guildId\":5,\"timeZone\":\"Europe/London\"}"),
            "/api/events/5" => Json("[{\"eventId\":1,\"title\":\"Raid night\",\"description\":\"Bring flasks\",\"scheduledTime\":\"2026-07-01T19:00:00Z\",\"type\":\"Generic\",\"status\":\"Scheduled\"}]"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var page = Render<GuildEvents>(parameters => parameters.Add(p => p.GuildId, "5"));

        page.WaitForAssertion(() => Assert.Contains("Jul 01, 2026 20:00", page.Find(".event-time").TextContent));
        Assert.Contains("Raid night", page.Markup);
    }

    [Fact]
    public void GuildEvents_UpcomingAndPastEvents_AreSplitAndEachRowShowsItsDurationSignupsAndRoles()
    {
        StartDashboard(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/settings/guild/5" => Json("{\"guildId\":5,\"timeZone\":\"UTC\"}"),
            "/api/events/5" => Json("[{\"eventId\":1,\"title\":\"Old raid\",\"description\":\"\",\"scheduledTime\":\"2020-01-01T19:00:00Z\",\"type\":\"Generic\",\"status\":\"Completed\"},"
                + "{\"eventId\":2,\"title\":\"Future raid\",\"description\":\"Bring flasks\",\"scheduledTime\":\"2099-01-01T19:00:00Z\",\"type\":\"WowClassic\",\"status\":\"Scheduled\",\"durationMinutes\":150,\"signedUpCount\":12,\"unsureCount\":3,\"mentionRoleIds\":[\"11\",\"12\"]}]"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var page = Render<GuildEvents>(parameters => parameters.Add(p => p.GuildId, "5"));

        page.WaitForElement(".event-row");
        var sections = page.FindAll(".events-section");
        Assert.Equal(2, sections.Count);
        Assert.Contains("Future raid", sections[0].TextContent);
        Assert.Contains("Old raid", sections[1].TextContent);
        var future = sections[0].TextContent;
        Assert.Contains("2h 30m", future);
        Assert.Contains("until 21:30", future);
        Assert.Contains("12 signed up, 3 unsure", future);
        Assert.Contains("2 roles pinged", future);
        Assert.Contains("WoW Classic", future);
    }

    [Fact]
    public void GuildEvents_GuildWithNoTimeZoneChosen_ShowsUtc()
    {
        StartDashboard(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/settings/guild/5" => Json("{\"guildId\":5,\"timeZone\":\"UTC\"}"),
            "/api/events/5" => Json("[{\"eventId\":1,\"title\":\"Raid night\",\"description\":\"\",\"scheduledTime\":\"2026-07-01T19:00:00Z\",\"type\":\"Generic\",\"status\":\"Scheduled\"}]"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var page = Render<GuildEvents>(parameters => parameters.Add(p => p.GuildId, "5"));

        page.WaitForAssertion(() => Assert.Contains("Jul 01, 2026 19:00", page.Find(".event-time").TextContent));
    }

    [Fact]
    public void GuildEvents_NoSession_SendsTheVisitorToTheLoginPageWithoutLoadingAnyEvents()
    {
        var api = StartDashboard(_ => new HttpResponseMessage(HttpStatusCode.NotFound), signedIn: false);

        var page = Render<GuildEvents>(parameters => parameters.Add(p => p.GuildId, "5"));

        Assert.EndsWith("/login", Services.GetRequiredService<NavigationManager>().Uri);
        Assert.True(string.IsNullOrWhiteSpace(page.Markup));
        Assert.Empty(api.Paths);
    }

    [Fact]
    public void GuildEvents_ApiRefusesTheRequest_ShowsTheReasonInsteadOfAnEmptyList()
    {
        StartDashboard(request => request.RequestUri!.AbsolutePath == "/api/events/5"
            ? Json("{\"error\":\"You do not have permission for this server.\"}", HttpStatusCode.Forbidden)
            : Json("{\"guildId\":5,\"timeZone\":\"UTC\"}"));

        var page = Render<GuildEvents>(parameters => parameters.Add(p => p.GuildId, "5"));

        page.WaitForAssertion(() => Assert.Contains("You do not have permission for this server.", page.Find(".error-message").TextContent));
        Assert.DoesNotContain("No events scheduled", page.Markup);
    }

    private static HttpResponseMessage CreateEventApi(HttpRequestMessage request, Func<HttpResponseMessage> onCreate) => request.RequestUri!.AbsolutePath switch
    {
        "/api/settings/guild/5" => Json("{\"guildId\":5,\"timeZone\":\"Europe/London\"}"),
        "/api/guilds/5/channels" => Json("[{\"id\":\"777\",\"name\":\"raids\",\"type\":0,\"position\":0}]"),
        "/api/guilds/5/roles" => Json("[{\"id\":\"11\",\"name\":\"Raiders\",\"position\":1}]"),
        "/api/events/5" when request.Method == HttpMethod.Post => onCreate(),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound)
    };

    [Fact]
    public void CreateEvent_FormFilledIn_SendsTheGuildsLocalStartTimeAsUtcAndOpensTheNewEvent()
    {
        string? body = null;
        StartDashboard(request => CreateEventApi(request, () =>
        {
            body = request.Content!.ReadAsStringAsync().Result;
            return Json("{\"eventId\":\"99\"}", HttpStatusCode.Created);
        }));
        var page = Render<CreateEvent>(parameters => parameters.Add(p => p.GuildId, "5"));
        page.WaitForElement("#event-title");

        page.Find("#event-title").Change("Raid night");
        page.Find("#event-time").Change("2026-07-01T20:00");   // 20:00 in London in summer is 19:00 UTC
        page.Find("#event-channel").Change("777");
        page.Find("#event-repeat").Change("Weekly");
        page.Find("#create-event").Click();

        page.WaitForAssertion(() => Assert.EndsWith("/guild/5/event/99", Services.GetRequiredService<NavigationManager>().Uri));
        Assert.Contains("\"scheduledTime\":\"2026-07-01T19:00:00Z\"", body);
        Assert.Contains("\"channelId\":777", body);
        Assert.Contains("\"recurrence\":\"Weekly\"", body);
    }

    [Fact]
    public void CreateEvent_ReminderInHours_IsSentAsMinutesAndLeftOutWhenBlank()
    {
        var bodies = new List<string>();
        StartDashboard(request => CreateEventApi(request, () =>
        {
            bodies.Add(request.Content!.ReadAsStringAsync().Result);
            return Json("{\"eventId\":\"99\"}", HttpStatusCode.Created);
        }));
        var page = Render<CreateEvent>(parameters => parameters.Add(p => p.GuildId, "5"));
        page.WaitForElement("#event-title");
        page.Find("#event-title").Change("Raid night");
        page.Find("#event-channel").Change("777");

        page.Find("#event-reminder").Change("24");
        page.Find("#create-event").Click();

        page.WaitForAssertion(() => Assert.Contains("\"reminderMinutes\":1440", Assert.Single(bodies)));
    }

    [Fact]
    public void CreateEvent_ReminderBlank_SendsNoReminder()
    {
        string? body = null;
        StartDashboard(request => CreateEventApi(request, () =>
        {
            body = request.Content!.ReadAsStringAsync().Result;
            return Json("{\"eventId\":\"99\"}", HttpStatusCode.Created);
        }));
        var page = Render<CreateEvent>(parameters => parameters.Add(p => p.GuildId, "5"));
        page.WaitForElement("#event-title");
        page.Find("#event-title").Change("Raid night");
        page.Find("#event-channel").Change("777");

        page.Find("#create-event").Click();

        page.WaitForAssertion(() => Assert.Contains("\"reminderMinutes\":null", body));
    }

    [Fact]
    public void CreateEvent_NoChannelChosen_AsksForOneWithoutCallingTheApi()
    {
        var api = StartDashboard(request => CreateEventApi(request, () => Json("{\"eventId\":\"99\"}", HttpStatusCode.Created)));
        var page = Render<CreateEvent>(parameters => parameters.Add(p => p.GuildId, "5"));
        page.WaitForElement("#event-title");

        page.Find("#event-title").Change("Raid night");
        page.Find("#create-event").Click();

        page.WaitForAssertion(() => Assert.Contains("Choose the channel", page.Find(".error-message").TextContent));
        Assert.DoesNotContain("/api/events/5", api.Paths);
    }

    [Fact]
    public void CreateEvent_ApiRefusesTheEvent_ShowsTheReasonAndStaysOnTheForm()
    {
        StartDashboard(request => CreateEventApi(request, () => Json("{\"title\":\"Bad Request\",\"status\":400,\"detail\":\"The scheduled time must be in the future.\"}", HttpStatusCode.BadRequest)));
        var page = Render<CreateEvent>(parameters => parameters.Add(p => p.GuildId, "5"));
        page.WaitForElement("#event-title");

        page.Find("#event-title").Change("Raid night");
        page.Find("#event-channel").Change("777");
        page.Find("#create-event").Click();

        page.WaitForAssertion(() => Assert.Contains("The scheduled time must be in the future.", page.Find(".error-message").TextContent));
        Assert.DoesNotContain("/event/", Services.GetRequiredService<NavigationManager>().Uri);
    }

    [Fact]
    public void Login_ReturnFromDiscordWithAStateThatWasNotStartedHere_IsRefusedAndNeverExchangesTheCode()
    {
        var api = StartDashboard(_ => Json(SignedInBody()));
        var login = new StubApi(_ => Json(SignedInBody()));
        var tokens = new TokenService();
        Services.AddSingleton(new AuthService(new HttpClient(login) { BaseAddress = new Uri("http://api.test") }, tokens));
        JSInterop.Setup<string?>("sessionStorage.getItem", _ => true).SetResult("the-state-this-tab-created");
        Services.GetRequiredService<NavigationManager>().NavigateTo("/callback?code=abc&state=someone-elses-state");

        var page = Render<Login>();

        page.WaitForAssertion(() => Assert.Contains("could not be verified", page.Find(".error-message").TextContent));
        Assert.Empty(login.Paths);
        Assert.Empty(api.Paths);
    }

    private static string EventJson(string status, bool recurring) =>
        "{\"eventId\":9,\"guildId\":5,\"title\":\"Raid night\",\"description\":\"\",\"scheduledTime\":\"2030-07-01T19:00:00Z\",\"type\":\"Generic\",\"status\":\"" + status + "\",\"isRecurring\":" + (recurring ? "true" : "false") + "}";

    private StubApi StartEventPage(bool recurring, Func<string> status)
    {
        return StartDashboard(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/events/byid/9" => Json(EventJson(status(), recurring)),
            "/api/settings/guild/5" => Json("{\"guildId\":5,\"timeZone\":\"UTC\"}"),
            "/api/guilds/5/roles" => Json("[]"),
            "/api/signups/event/9" => Json("[]"),
            "/api/events/9/cancel" => new HttpResponseMessage(HttpStatusCode.NoContent),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
    }

    private IRenderedComponent<EventDetailPage> RenderEventPage()
    {
        return Render<EventDetailPage>(parameters => parameters.Add(p => p.GuildId, "5").Add(p => p.EventId, "9"));
    }

    [Fact]
    public void EventDetail_ScheduledOneOffEvent_CancelsItOnConfirmAndShowsItCancelled()
    {
        var status = "Scheduled";
        var api = StartEventPage(recurring: false, () => status);
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);
        var page = RenderEventPage();
        page.WaitForElement("#cancel-event");

        status = "Cancelled";
        page.Find("#cancel-event").Click();

        page.WaitForAssertion(() => Assert.Contains("Event cancelled.", page.Find(".success-message").TextContent));
        Assert.Contains("/api/events/9/cancel", api.Paths);
        Assert.Empty(page.FindAll("#cancel-event"));
    }

    [Fact]
    public void EventDetail_CancelNotConfirmed_DoesNotCallTheApi()
    {
        var api = StartEventPage(recurring: false, () => "Scheduled");
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(false);
        var page = RenderEventPage();
        page.WaitForElement("#cancel-event");

        page.Find("#cancel-event").Click();

        Assert.DoesNotContain("/api/events/9/cancel", api.Paths);
    }

    [Fact]
    public void EventDetail_RecurringEvent_OffersTheSeriesCancelNotASingleCancel()
    {
        StartEventPage(recurring: true, () => "Scheduled");
        var page = RenderEventPage();
        page.WaitForElement("#delete-event");

        Assert.Empty(page.FindAll("#cancel-event"));
        Assert.Contains("Cancel Series", page.Markup);
    }

    [Fact]
    public void EventDetail_CancelledEvent_OffersNoActionButtons()
    {
        StartEventPage(recurring: false, () => "Cancelled");
        var page = RenderEventPage();
        page.WaitForAssertion(() => Assert.Contains("Raid night", page.Markup));

        Assert.Empty(page.FindAll("#cancel-event"));
        Assert.Empty(page.FindAll("#delete-event"));
    }

    private static HttpResponseMessage GuildHomeApi(HttpRequestMessage request) => request.RequestUri!.AbsolutePath switch
    {
        "/api/guilds/5" => Json("{\"id\":\"5\",\"name\":\"Raiders\",\"icon\":\"abc\",\"description\":\"We raid\",\"memberCount\":42,\"createdAt\":\"2020-01-02T00:00:00Z\",\"ownerId\":\"7\",\"ownerName\":\"Guild Master\"}"),
        "/api/guilds/5/statistics" => Json("{\"guildId\":5,\"guildName\":\"Raiders\",\"memberCount\":42,\"totalEvents\":3,\"upcomingEvents\":1,\"rolesCount\":9,\"channelsCount\":5,\"createdAt\":\"2020-01-02T00:00:00Z\",\"boostLevel\":0}"),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound)
    };

    [Fact]
    public void GuildHome_SignedIn_ShowsTheGuildsIconDescriptionOwnerAndStatistics()
    {
        var api = StartDashboard(GuildHomeApi);

        var page = Render<GuildHome>(parameters => parameters.Add(p => p.GuildId, "5"));

        page.WaitForAssertion(() => Assert.Contains("We raid", page.Find(".guild-header").TextContent));
        Assert.Contains("Guild Master", page.Find(".guild-meta").TextContent);
        Assert.Equal("https://cdn.discordapp.com/icons/5/abc.png", page.Find(".guild-header img").GetAttribute("src"));
        Assert.Contains("Total Events", page.Markup);
        Assert.DoesNotContain("/api/guilds", api.Paths);
    }

    [Fact]
    public void GuildHome_ApiRefusesTheGuild_ShowsTheReasonAndNoStatistics()
    {
        var api = StartDashboard(request => request.RequestUri!.AbsolutePath == "/api/guilds/5"
            ? Json("{\"error\":\"You do not have permission for this server.\"}", HttpStatusCode.Forbidden)
            : new HttpResponseMessage(HttpStatusCode.NotFound));

        var page = Render<GuildHome>(parameters => parameters.Add(p => p.GuildId, "5"));

        page.WaitForAssertion(() => Assert.Contains("You do not have permission for this server.", page.Find(".error-message").TextContent));
        Assert.DoesNotContain("/api/guilds/5/statistics", api.Paths);
    }

    private const string AliceSignup = "{\"signupId\":1,\"userId\":100,\"displayName\":\"Alice\",\"status\":\"SignedUp\",\"wowClass\":\"paladin\",\"wowSpec\":\"holy\",\"signupOrder\":1,\"signedUpAt\":\"2030-06-01T10:00:00Z\"}";

    private const string BobUnsure = "{\"signupId\":2,\"userId\":200,\"displayName\":\"Bob\",\"status\":\"Unsure\",\"signedUpAt\":\"2030-06-01T11:00:00Z\"}";

    private List<(string Method, string Path, string Body)> StartWowEventPage(string signupsJson)
    {
        var calls = new List<(string Method, string Path, string Body)>();

        StartDashboard(request =>
        {
            var body = request.Content == null ? "" : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (request.Method != HttpMethod.Get)
            {
                calls.Add((request.Method.Method, request.RequestUri!.AbsolutePath, body));
            }

            return request.RequestUri!.AbsolutePath switch
            {
                "/api/events/byid/9" => Json(EventJson("Scheduled", false).Replace("\"Generic\"", "\"WowClassic\"")),
                "/api/settings/guild/5" => Json("{\"guildId\":5,\"timeZone\":\"UTC\"}"),
                "/api/guilds/5/roles" => Json("[]"),
                "/api/signups/event/9" => Json("[" + signupsJson + "]"),
                "/api/wow/icons" => Json("{\"paladin\":\"https://cdn.test/paladin.png\",\"paladin_holy\":\"https://cdn.test/paladin_holy.png\",\"mage\":\"https://cdn.test/mage.png\",\"mage_fire\":\"https://cdn.test/mage_fire.png\"}"),
                "/api/guilds/5/members" => Json("[{\"id\":300,\"displayName\":\"Carol\",\"username\":\"carol\"},{\"id\":100,\"displayName\":\"Alice\",\"username\":\"alice\"}]"),
                "/api/signups/event/9/user/100" or "/api/signups/event/9/user/200" => new HttpResponseMessage(HttpStatusCode.NoContent),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });

        return calls;
    }

    [Fact]
    public void EventDetail_WowEvent_ShowsTheSpecIconBesideEachSignup()
    {
        StartWowEventPage(AliceSignup);

        var page = RenderEventPage();

        page.WaitForAssertion(() => Assert.Equal("https://cdn.test/paladin_holy.png", page.Find(".signup-item .wow-icon").GetAttribute("src")));
    }

    [Fact]
    public void EventDetail_AddPlayer_SearchesPicksAClassAndSpecAndSignsThemUp()
    {
        var calls = StartWowEventPage(AliceSignup);
        var page = RenderEventPage();
        page.WaitForElement("#add-player-toggle").Click();

        page.WaitForElement("#member-300").Click();
        page.Find("#add-class-mage").Click();
        page.Find("#add-spec-fire").Click();
        page.Find("#add-player-submit").Click();

        page.WaitForAssertion(() => Assert.Contains("Carol added to the event.", page.Find(".success-message").TextContent));
        var call = Assert.Single(calls);
        Assert.Equal(("POST", "/api/signups/event/9"), (call.Method, call.Path));
        Assert.Contains("\"userId\":300", call.Body);
        Assert.Contains("\"wowClass\":\"mage\"", call.Body);
        Assert.Contains("\"wowSpec\":\"fire\"", call.Body);
        Assert.Empty(page.FindAll(".add-player-panel"));
    }

    [Fact]
    public void EventDetail_AddPlayer_ShowsWhoIsAlreadySignedUp()
    {
        StartWowEventPage(AliceSignup);
        var page = RenderEventPage();
        page.WaitForElement("#add-player-toggle").Click();

        page.WaitForElement("#member-100");

        Assert.Contains("already signed up", page.Find("#member-100").TextContent);
        Assert.DoesNotContain("already signed up", page.Find("#member-300").TextContent);
    }

    [Fact]
    public void EventDetail_AddPlayer_ServerRefusesThem_ShowsTheReasonAndKeepsThePanelOpen()
    {
        StartDashboard(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/events/byid/9" => Json(EventJson("Scheduled", false)),
            "/api/settings/guild/5" => Json("{\"guildId\":5,\"timeZone\":\"UTC\"}"),
            "/api/guilds/5/roles" => Json("[]"),
            "/api/signups/event/9" when request.Method == HttpMethod.Post => Json("{\"title\":\"Bad Request\",\"status\":400,\"detail\":\"That person is not in this server.\"}", HttpStatusCode.BadRequest),
            "/api/signups/event/9" => Json("[]"),
            "/api/guilds/5/members" => Json("[{\"id\":300,\"displayName\":\"Carol\",\"username\":\"carol\"}]"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var page = RenderEventPage();
        page.WaitForElement("#add-player-toggle").Click();
        page.WaitForElement("#member-300").Click();

        page.Find("#add-player-submit").Click();

        page.WaitForAssertion(() => Assert.Contains("That person is not in this server.", page.Find(".add-player-panel .error-message").TextContent));
    }

    [Fact]
    public void EventDetail_EditSignup_ChangesTheStatusOnly()
    {
        var calls = StartWowEventPage(AliceSignup + "," + BobUnsure);
        var page = RenderEventPage();
        page.WaitForElement("#edit-signup-200").Click();
        Assert.True(page.Find("#signup-edit-save").HasAttribute("disabled"));

        page.Find("#status-SignedOff").Click();
        page.Find("#signup-edit-save").Click();

        page.WaitForAssertion(() => Assert.Contains("Signup updated.", page.Find(".success-message").TextContent));
        var call = Assert.Single(calls);
        Assert.Equal(("PATCH", "/api/signups/event/9/user/200"), (call.Method, call.Path));
        Assert.Contains("\"status\":\"SignedOff\"", call.Body);
        Assert.Contains("\"wowClass\":null", call.Body);
    }

    [Fact]
    public void EventDetail_EditSignup_ChangesTheClassAndSpec()
    {
        var calls = StartWowEventPage(AliceSignup);
        var page = RenderEventPage();
        page.WaitForElement("#edit-signup-100").Click();

        page.Find("#class-mage").Click();
        Assert.True(page.Find("#signup-edit-save").HasAttribute("disabled"));
        page.Find("#spec-fire").Click();
        page.Find("#signup-edit-save").Click();

        page.WaitForAssertion(() => Assert.Contains("Signup updated.", page.Find(".success-message").TextContent));
        var call = Assert.Single(calls);
        Assert.Equal(("PATCH", "/api/signups/event/9/user/100"), (call.Method, call.Path));
        Assert.Contains("\"wowClass\":\"mage\"", call.Body);
        Assert.Contains("\"wowSpec\":\"fire\"", call.Body);
        Assert.Contains("\"status\":null", call.Body);
    }

    [Fact]
    public void EventDetail_EditSignup_ClassAndSpecOnlyOfferedToSomeoneSignedUp()
    {
        StartWowEventPage(AliceSignup);
        var page = RenderEventPage();
        page.WaitForElement("#edit-signup-100").Click();
        Assert.NotEmpty(page.FindAll("#class-mage"));

        page.Find("#status-Unsure").Click();

        Assert.Empty(page.FindAll("#class-mage"));
    }

    [Fact]
    public void EventDetail_CancelledEvent_OffersNoWayToAddOrEditSignups()
    {
        StartEventPage(recurring: false, () => "Cancelled");
        var page = RenderEventPage();
        page.WaitForAssertion(() => Assert.Contains("Raid night", page.Markup));

        Assert.Empty(page.FindAll("#add-player-toggle"));
    }

    private AuthService StartLoginPage(Func<HttpRequestMessage, HttpResponseMessage> api)
    {
        StartDashboard(_ => Json(SignedInBody()));
        var auth = new AuthService(new HttpClient(new StubApi(api)) { BaseAddress = new Uri("http://api.test") }, new TokenService());
        Services.AddSingleton(auth);

        return auth;
    }

    [Fact]
    public void Login_SignInClicked_SendsTheVisitorToDiscordWithTheClientIdTheApiGives()
    {
        StartLoginPage(request => request.RequestUri!.AbsolutePath == "/api/config"
            ? Json("{\"discordClientId\":\"app-123\"}")
            : new HttpResponseMessage(HttpStatusCode.NotFound));
        var page = Render<Login>();

        page.Find(".btn-discord").Click();

        page.WaitForAssertion(() => Assert.StartsWith("https://discord.com/api/oauth2/authorize?client_id=app-123&", Services.GetRequiredService<NavigationManager>().Uri));
    }

    [Fact]
    public void Login_ApiDoesNotAnswer_SaysSoInsteadOfSendingTheVisitorAnywhere()
    {
        StartLoginPage(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var page = Render<Login>();

        page.Find(".btn-discord").Click();

        page.WaitForAssertion(() => Assert.Contains("API", page.Find(".error-message").TextContent));
        Assert.DoesNotContain("discord.com", Services.GetRequiredService<NavigationManager>().Uri);
    }
}
