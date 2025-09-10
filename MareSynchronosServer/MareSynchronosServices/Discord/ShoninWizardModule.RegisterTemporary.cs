using System.Text;
using Discord;
using Discord.Interactions;
using MareSynchronosShared.Models;
using MareSynchronosShared.Utils.Configuration;
using Microsoft.EntityFrameworkCore;

namespace MareSynchronosServices.Discord;

public partial class ShoninWizardModule
{
    [ComponentInteraction("temp-wizard-captcha:*")]
    public async Task TempWizardCaptcha(bool init = false)
    {
        if (!init && !(await ValidateInteraction().ConfigureAwait(false))) return;

        if (_botServices.VerifiedCaptchaUsers.Contains(Context.Interaction.User.Id))
        {
            await StartTempWizard(true).ConfigureAwait(false);
            return;
        }

        EmbedBuilder eb = new();

        Random rnd = new Random();
        var correctButton = rnd.Next(4) + 1;
        string nthButtonText = correctButton switch
        {
            1 => "first",
            2 => "second",
            3 => "third",
            4 => "fourth",
            _ => "unknown",
        };

        Emoji nthButtonEmoji = correctButton switch
        {
            1 => new Emoji("⬅️"),
            2 => new Emoji("🤖"),
            3 => new Emoji("‼️"),
            4 => new Emoji("✉️"),
            _ => "unknown",
        };

        eb.WithTitle("Shonin Sync Bot Services Captcha");
        eb.WithDescription("You are seeing this embed because you interact with this bot for the first time since the bot has been restarted." + Environment.NewLine + Environment.NewLine
            + "This bot __requires__ embeds for its function. To proceed, please verify you have embeds enabled." + Environment.NewLine
            + $"## To verify you have embeds enabled __press on the **{nthButtonText}** button ({nthButtonEmoji}).__");
        eb.WithColor(Color.LightOrange);

        int incorrectButtonHighlight = 1;
        do
        {
            incorrectButtonHighlight = rnd.Next(4) + 1;
        }
        while (incorrectButtonHighlight == correctButton);

        ComponentBuilder cb = new();
        cb.WithButton("This", correctButton == 1 ? "temp-wizard-home:false" : "temp-wizard-captcha-fail:1", emote: new Emoji("⬅️"), style: incorrectButtonHighlight == 1 ? ButtonStyle.Primary : ButtonStyle.Secondary);
        cb.WithButton("Bot", correctButton == 2 ? "temp-wizard-home:false" : "temp-wizard-captcha-fail:2", emote: new Emoji("🤖"), style: incorrectButtonHighlight == 2 ? ButtonStyle.Primary : ButtonStyle.Secondary);
        cb.WithButton("Requires", correctButton == 3 ? "temp-wizard-home:false" : "temp-wizard-captcha-fail:3", emote: new Emoji("‼️"), style: incorrectButtonHighlight == 3 ? ButtonStyle.Primary : ButtonStyle.Secondary);
        cb.WithButton("Embeds", correctButton == 4 ? "temp-wizard-home:false" : "temp-wizard-captcha-fail:4", emote: new Emoji("✉️"), style: incorrectButtonHighlight == 4 ? ButtonStyle.Primary : ButtonStyle.Secondary);

        await InitOrUpdateInteraction(init, eb, cb).ConfigureAwait(false);
    }
    
    
    [ComponentInteraction("temp-wizard-captcha-fail:*")]
    public async Task TempWizardCaptchaFail(int button)
    {
        ComponentBuilder cb = new();
        cb.WithButton("Restart (with Embeds enabled)", "wizard-captcha:false", emote: new Emoji("↩️"));
        await ((Context.Interaction) as IComponentInteraction).UpdateAsync(m =>
        {
            m.Embed = null;
            m.Content = "You pressed the wrong button. You likely have embeds disabled. Enable embeds in your Discord client (Settings -> Chat -> \"Show embeds and preview website links pasted into chat\") and try again.";
            m.Components = cb.Build();
        }).ConfigureAwait(false);

        await _botServices.LogToChannel($"{Context.User.Mention} FAILED CAPTCHA").ConfigureAwait(false);
    }
    
    [ComponentInteraction("temp-wizard-home:*")]
    public async Task StartTempWizard(bool init = false)
    {
        _logger.LogInformation("Starting Temp Wizard");
        if (!init && !(await ValidateInteraction().ConfigureAwait(false))) return;

        if (!_botServices.VerifiedCaptchaUsers.Contains(Context.Interaction.User.Id))
            _botServices.VerifiedCaptchaUsers.Add(Context.Interaction.User.Id);

        _logger.LogInformation("{method}:{userId}", nameof(StartTempWizard), Context.Interaction.User.Id);

        using var mareDb = await GetDbContext().ConfigureAwait(false);
        var account = await mareDb.LodeStoneAuth
            .Include(x => x.User)
            .FirstOrDefaultAsync(u => u.DiscordId == Context.User.Id && u.StartedAt == null).ConfigureAwait(false);
        var hasAccount = account != null;
        EmbedBuilder eb = new();
        ComponentBuilder cb = new();
        var isPermanentUser = hasAccount && (!account.User?.IsLimitedUser ?? false);
        var isLimitedUser = hasAccount && (account.User?.IsLimitedUser ?? false);
        eb.WithTitle("Welcome to the Shonin Sync Service Bot for this server");
        eb.WithDescription("Here is what you can do:" + Environment.NewLine + Environment.NewLine
                           + (!isLimitedUser ? string.Empty : ("- Check your account status press \"ℹ️ User Info\"" + Environment.NewLine))
                           + (!hasAccount ? string.Empty : ("- Join this community's Syncshell \"🌒 Join Syncshell\"" + Environment.NewLine))
                           + (hasAccount ? string.Empty : ("- Register a new Temporary Shonin Sync Account press \"🌒 Register\"" + Environment.NewLine))
        );
        eb.WithColor(Color.Blue);
        if (!hasAccount)
        {
            cb.WithButton("Register", "temp-wizard-register", ButtonStyle.Primary, new Emoji("🌒"));
        }
        else
        {
            if (isLimitedUser)
            {
                cb.WithButton("User Info", "temp-wizard-userinfo", ButtonStyle.Secondary,
                    new Emoji("ℹ️"));
            }
            cb.WithButton("Join Syncshell", "temp-wizard-joinsyncshell", ButtonStyle.Secondary,
                    new Emoji("🌒"));
        }
        
        _logger.LogDebug("Embed built");
        await InitOrUpdateInteraction(init, eb, cb).ConfigureAwait(false);
    }

    [ComponentInteraction("temp-wizard-joinsyncshell")]
    public async Task TempWizardJoinSyncShell()
    {
        await DeferAsync().ConfigureAwait(false);
        using var db = await _dbContextFactory.CreateDbContextAsync().ConfigureAwait(false);
        var lodestone = await db.LodeStoneAuth
            .Include(x => x.User)
            .FirstOrDefaultAsync(x => x.DiscordId == Context.User.Id).ConfigureAwait(false);
        if (lodestone == null)
        {
            var errorEb = new EmbedBuilder();
            errorEb.WithColor(Color.Red);
            errorEb.WithTitle("Error");
            errorEb.WithDescription("Your Shonin Sync Account somehow doesn't exist. Please try again.");
            await FollowupAsync(null, null, false, true, null, null, null, errorEb.Build())
                .ConfigureAwait(false);
            return;
        }

        var serverConfigs =
            _mareServicesConfiguration.GetValueOrDefault<IList<DiscordServerConfiguration>>(
                nameof(ServicesConfiguration.ServerConfigurations),
                new List<DiscordServerConfiguration>());
        var serverConfig = serverConfigs.FirstOrDefault(x => x.ServerId == Context.Guild.Id);
        if (serverConfig == null)
        {
            var errorEb = new EmbedBuilder();
            errorEb.WithColor(Color.Red);
            errorEb.WithTitle("Error");
            errorEb.WithDescription("This community is not properly configured. File a bug report.");
            await FollowupAsync(null, null, false, true, null, null, null, errorEb.Build())
                .ConfigureAwait(false);
            return;
        }

        var syncshell = await db.Groups
            .FirstOrDefaultAsync(x => x.Alias.Equals(serverConfig.SyncshellVanityId))
            .ConfigureAwait(false);
        if (syncshell == null)
        {
            var errorEb = new EmbedBuilder();
            errorEb.WithColor(Color.Red);
            errorEb.WithTitle("Error");
            errorEb.WithDescription("This community is not properly configured. File a bug report.");
            await FollowupAsync(null, null, false, true, null, null, null, errorEb.Build())
                .ConfigureAwait(false);
            return;
        }

        await _syncshellManager.JoinSyncshell(syncshell.GID, lodestone.User!.UID).ConfigureAwait(false);
        var eb = new EmbedBuilder();
        eb.WithColor(Color.Green);
        eb.WithTitle("Success!");
        eb.WithDescription("You have been added to this community's Sync Shell. You may need to re-connect to ShoninSync to see it.");
        await FollowupAsync(null, null, false, true, null, null, null, eb.Build())
            .ConfigureAwait(false);
    }
    
    [ComponentInteraction("temp-wizard-userinfo")]
    public async Task ComponentTempUserinfo()
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}", nameof(ComponentUserinfo), Context.Interaction.User.Id);

        using var mareDb = await GetDbContext().ConfigureAwait(false);
        EmbedBuilder eb = new();
        eb.WithTitle("User Info");
        eb.WithColor(Color.Blue);
        eb.WithDescription("You can see information about your user account(s) here." + Environment.NewLine
            + "Use the selection below to select a user account to see info for." + Environment.NewLine + Environment.NewLine
            + "- 1️⃣ is your primary account/UID" + Environment.NewLine
            + "- 2️⃣ are all your secondary accounts/UIDs" + Environment.NewLine
            + "If you are using Vanity UIDs the original UID is displayed in the second line of the account selection.");
        ComponentBuilder cb = new();
        await AddUserSelection(mareDb, cb, "wizard-userinfo-select").ConfigureAwait(false);
        AddHome(cb);
        await ModifyInteraction(eb, cb).ConfigureAwait(false);
    }
    
    [ComponentInteraction("temp-wizard-register")]
    public async Task ComponentTempRegister()
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}", nameof(ComponentTempRegister), Context.Interaction.User.Id);

        EmbedBuilder eb = new();
        eb.WithColor(Color.Blue);
        eb.WithTitle("Start Registration");
        var sb = new StringBuilder("Click the button below to get temporary access to Shonin Sync");
        sb.AppendLine();
        sb.AppendLine(
            "Temporary Accounts are very limited. You will not be able to manually pair, and you will not be able to create or join synchshells.");
        sb.AppendLine(
            "You will be automatically joined to the appropriate SyncShell for the community you are getting Temporary Access to.");
        sb.AppendLine("Your account will only exist for a limited amount of time, after which you will need to renew it.");
        eb.WithDescription("Click the button below to get temporary access to Shonin Sync");
        ComponentBuilder cb = new();
        AddHome(cb);
        cb.WithButton("Start Registration", "temp-wizard-register-start", ButtonStyle.Primary, emote: new Emoji("🌒"));
        await ModifyInteraction(eb, cb).ConfigureAwait(false);
    }
    
    [ComponentInteraction("temp-wizard-register-start")]
    public async Task ComponentTempRegisterStart()
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}", nameof(ComponentTempRegisterStart), Context.Interaction.User.Id);

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
        await ComponentTempRegisterVerifyCheck().ConfigureAwait(false);
    }
    
    public async Task ComponentTempRegisterVerifyCheck()
    {
        if (!(await ValidateInteraction().ConfigureAwait(false))) return;

        _logger.LogInformation("{method}:{userId}", nameof(ComponentTempRegisterVerifyCheck), Context.Interaction.User.Id);
        await DeferAsync().ConfigureAwait(false);
        EmbedBuilder eb = new();
        ComponentBuilder cb = new();
        eb.WithColor(Color.Green);
        using var db = await GetDbContext().ConfigureAwait(false);
        var (uid, key) = await HandleAddUser(db, true).ConfigureAwait(false);
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
        await FollowupAsync(null, null, false, true, null, null, cb.Build(), eb.Build())
            .ConfigureAwait(false);
    }
}