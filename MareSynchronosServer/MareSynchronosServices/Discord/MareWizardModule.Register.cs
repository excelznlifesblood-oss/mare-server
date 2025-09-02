using Discord.Interactions;
using Discord;
using MareSynchronosShared.Data;
using Microsoft.EntityFrameworkCore;
using MareSynchronosShared.Utils;
using MareSynchronosShared.Models;
using MareSynchronosShared.Services;
using MareSynchronosShared.Utils.Configuration;
using Discord.Rest;
using Discord.WebSocket;

namespace MareSynchronosServices.Discord;

public partial class MareWizardModule
{
    [ComponentInteraction("wizard-register")]
    public async Task ComponentRegister()
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}", nameof(ComponentRegister), Context.Interaction.User.Id);

        EmbedBuilder eb = new();
        eb.WithColor(Color.Blue);
        eb.WithTitle("Start Registration");
        eb.WithDescription("Here you can start the registration process with the Shonin Sync server of this Discord." + Environment.NewLine + Environment.NewLine
            + "# Follow the bot instructions precisely. Slow down and read.");
        ComponentBuilder cb = new();
        AddHome(cb);
        cb.WithButton("Start Registration", "wizard-register-start", ButtonStyle.Primary, emote: new Emoji("🌒"));
        await ModifyInteraction(eb, cb).ConfigureAwait(false);
    }

    [ComponentInteraction("wizard-register-start")]
    public async Task ComponentRegisterStart()
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}", nameof(ComponentRegisterStart), Context.Interaction.User.Id);

        using var db = await GetDbContext().ConfigureAwait(false);
        var entry = await db.LodeStoneAuth.SingleOrDefaultAsync(u => u.DiscordId == Context.User.Id && u.StartedAt != null).ConfigureAwait(false);
        if (entry != null)
        {
            db.LodeStoneAuth.Remove(entry);
        }
        _botServices.DiscordLodestoneMapping.TryRemove(Context.User.Id, out _);
        _botServices.DiscordVerifiedUsers.TryRemove(Context.User.Id, out _);
        LodeStoneAuth lsAuth = new LodeStoneAuth()
        {
            DiscordId = Context.User.Id,
            StartedAt = DateTime.UtcNow
        };

        db.Add(lsAuth);
        await db.SaveChangesAsync().ConfigureAwait(false);
        await ComponentRegisterVerifyCheck().ConfigureAwait(false);
    }

    public async Task ComponentRegisterVerifyCheck()
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}", nameof(ComponentRegisterVerifyCheck), Context.Interaction.User.Id);

        EmbedBuilder eb = new();
        ComponentBuilder cb = new();
        eb.WithColor(Color.Green);
        using var db = await GetDbContext().ConfigureAwait(false);
        var (uid, key) = await HandleAddUser(db).ConfigureAwait(false);
        eb.WithTitle($"Registration successful, your UID: {uid}");
        eb.WithDescription("This is your private secret key. Do not share this private secret key with anyone. **If you lose it, it is irrevocably lost.**"
                           + Environment.NewLine + Environment.NewLine
                           + "**__NOTE: Secret keys are considered legacy. Using the suggested OAuth2 authentication in Shonin Sync, you do not need to use this Secret Key.__**"
                           + Environment.NewLine + Environment.NewLine
                           + $"||**`{key}`**||"
                           + Environment.NewLine + Environment.NewLine
                           + "If you want to continue using legacy authentication, enter this key in Shonin Sync and hit save to connect to the service."
                           + Environment.NewLine
                           + "__NOTE: The Secret Key only contains the letters ABCDEF and numbers 0 - 9.__"
                           + Environment.NewLine
                           + "You should connect as soon as possible to not get caught by the automatic cleanup process."
                           + Environment.NewLine
                           + "Have fun.");
        AddHome(cb);
        await ModifyInteraction(eb, cb).ConfigureAwait(false);
        await _botServices.AddRegisteredRoleAsync(Context.Interaction.User).ConfigureAwait(false);
    }

    private async Task<(string, string)> HandleAddUser(MareDbContext db)
    {
        var lodestoneAuth = db.LodeStoneAuth.SingleOrDefault(u => u.DiscordId == Context.User.Id);

        var user = new User();

        var hasValidUid = false;
        while (!hasValidUid)
        {
            var uid = StringUtils.GenerateRandomString(10);
            if (db.Users.Any(u => u.UID == uid || u.Alias == uid)) continue;
            user.UID = uid;
            hasValidUid = true;
        }

        // make the first registered user on the service to admin
        if (!await db.Users.AnyAsync().ConfigureAwait(false))
        {
            user.IsAdmin = true;
        }

        user.LastLoggedIn = DateTime.UtcNow;

        var computedHash = StringUtils.Sha256String(StringUtils.GenerateRandomString(64) + DateTime.UtcNow.ToString());
        string hashedKey = StringUtils.Sha256String(computedHash);
        var auth = new Auth()
        {
            HashedKey = hashedKey,
            User = user,
        };

        await db.Users.AddAsync(user).ConfigureAwait(false);
        await db.Auth.AddAsync(auth).ConfigureAwait(false);

        lodestoneAuth.StartedAt = null;
        lodestoneAuth.User = user;

        await db.SaveChangesAsync().ConfigureAwait(false);

        _botServices.Logger.LogInformation("User registered: {userUID}:{hashedKey}", user.UID, hashedKey);

        await _botServices.LogToChannel($"{Context.User.Mention} REGISTER COMPLETE: => {user.UID}").ConfigureAwait(false);

        _botServices.DiscordVerifiedUsers.Remove(Context.User.Id, out _);

        return (user.UID, computedHash);
    }
}
