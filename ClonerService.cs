using System.Text.Json.Nodes;

namespace DiscordServerCloner;

public sealed class ClonerService
{
    private readonly DiscordApi _api;
    private readonly Action<string, LogLevel> _log;

    public ClonerService(DiscordApi api, Action<string, LogLevel> log)
    {
        _api = api;
        _log = log;
    }

    public enum LogLevel { Info, Success, Warn, Error }

    private static readonly System.Text.RegularExpressions.Regex TicketRegex =
        new(@"(^tickets?$|ticket[-_]|[-_]ticket|needed|handling|^[a-z]+-\d{4,6}$)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static bool IsTicketChannel(string name) => TicketRegex.IsMatch(name);

    public async Task<string> CloneAsync(string sourceGuildId,
        ClonerOptions options, CancellationToken ct)
    {
        Log("Fetching source server info…");
        var srcGuild = await _api.GetGuildAsync(sourceGuildId, ct);
        var srcName  = srcGuild["name"]?.GetValue<string>() ?? "Unknown";
        Log($"Source server: {srcName}");

        string? iconDataUri   = null;
        string? bannerDataUri = null;
        string? splashDataUri = null;

        if (options.CloneIcon)
        {
            var iconHash = srcGuild["icon"]?.GetValue<string>();
            if (iconHash is not null)
            {
                var isAnimated = iconHash.StartsWith("a_");
                var ext = isAnimated ? "gif" : "png";
                var url = $"https://cdn.discordapp.com/icons/{sourceGuildId}/{iconHash}.{ext}?size=1024";
                Log("Downloading server icon…");
                iconDataUri = await _api.DownloadImageAsDataUriAsync(url, ct);
                if (iconDataUri is not null) Log("Icon downloaded.", LogLevel.Success);
                else Log("Could not download icon.", LogLevel.Warn);
            }

            var bannerHash = srcGuild["banner"]?.GetValue<string>();
            if (bannerHash is not null)
            {
                var url = $"https://cdn.discordapp.com/banners/{sourceGuildId}/{bannerHash}.png?size=1024";
                Log("Downloading banner…");
                bannerDataUri = await _api.DownloadImageAsDataUriAsync(url, ct);
            }

            var splashHash = srcGuild["splash"]?.GetValue<string>();
            if (splashHash is not null)
            {
                var url = $"https://cdn.discordapp.com/splashes/{sourceGuildId}/{splashHash}.png?size=1024";
                Log("Downloading invite splash…");
                splashDataUri = await _api.DownloadImageAsDataUriAsync(url, ct);
            }
        }

        string newGuildId;

        if (!string.IsNullOrWhiteSpace(options.TargetGuildId))
        {
            newGuildId = options.TargetGuildId.Trim();
            Log($"Using existing server (ID: {newGuildId})", LogLevel.Success);
        }
        else
        {
            var rawName   = srcName;
            var finalName = rawName.Length > 100 ? rawName[..100] : rawName;
            if (finalName != rawName) Log("Server name truncated to 100 chars.", LogLevel.Warn);
            Log($"Creating new server \"{finalName}\"…");
            var newGuild = await _api.CreateGuildAsync(finalName, iconDataUri, ct);
            newGuildId   = newGuild["id"]!.GetValue<string>();
            Log($"New server created (ID: {newGuildId})", LogLevel.Success);

            await Task.Delay(2000, ct);

            if (options.CloneChannels)
            {
                Log("Removing default channels…");
                var defaultChannels = await _api.GetChannelsAsync(newGuildId, ct);
                foreach (var ch in defaultChannels)
                {
                    var chId = ch?["id"]?.GetValue<string>();
                    if (chId is null) continue;
                    try { await _api.DeleteChannelAsync(chId, ct); }
                    catch { }
                    await Task.Delay(300, ct);
                }
            }
        }

        var roleIdMap = new Dictionary<string, string>();

        if (options.CloneRoles)
        {
            Log("Cloning roles…");
            var srcRoles = await _api.GetRolesAsync(sourceGuildId, ct);

            var sorted = srcRoles
                .Where(r => r is not null)
                .OrderByDescending(r => r!["position"]?.GetValue<int>() ?? 0)
                .ToList();

            foreach (var role in sorted)
            {
                if (role is null) continue;
                var roleId   = role["id"]!.GetValue<string>();
                var roleName = role["name"]?.GetValue<string>() ?? "role";

                if (roleId == sourceGuildId)
                {
                    try
                    {
                        await _api.PatchRoleAsync(newGuildId, newGuildId, new
                        {
                            permissions = role["permissions"]?.GetValue<string>() ?? "0",
                        }, ct);
                        roleIdMap[roleId] = newGuildId;
                        Log($"  Updated @everyone permissions", LogLevel.Success);
                    }
                    catch (Exception ex) { Log($"  @everyone: {ex.Message}", LogLevel.Warn); }
                    await Task.Delay(400, ct);
                    continue;
                }

                try
                {
                    var newRole = await _api.CreateRoleAsync(newGuildId, new
                    {
                        name        = roleName,
                        color       = role["color"]?.GetValue<int>() ?? 0,
                        hoist       = role["hoist"]?.GetValue<bool>() ?? false,
                        mentionable = role["mentionable"]?.GetValue<bool>() ?? false,
                        permissions = role["permissions"]?.GetValue<string>() ?? "0",
                    }, ct);

                    var newRoleId = newRole["id"]!.GetValue<string>();
                    roleIdMap[roleId] = newRoleId;
                    Log($"  Role \"{roleName}\" cloned", LogLevel.Success);
                }
                catch (Exception ex)
                {
                    Log($"  Role \"{roleName}\" failed: {ex.Message}", LogLevel.Warn);
                }

                await Task.Delay(400, ct);
            }
        }

        var channelIdMap = new Dictionary<string, string>();
        JsonArray? srcChannels = null;

        if (options.CloneChannels)
        {
            Log("Cloning channels…");
            srcChannels = await _api.GetChannelsAsync(sourceGuildId, ct);

            var categories = srcChannels
                .Where(c => c?["type"]?.GetValue<int>() == 4)
                .OrderBy(c => c?["position"]?.GetValue<int>() ?? 0)
                .ToList();

            foreach (var cat in categories)
            {
                if (cat is null) continue;
                var catId   = cat["id"]!.GetValue<string>();
                var catName = cat["name"]?.GetValue<string>() ?? "category";

                try
                {
                    var overwrites = BuildOverwriteList(cat["permission_overwrites"] as JsonArray, roleIdMap);
                    var newCat = await _api.CreateChannelAsync(newGuildId, new
                    {
                        name                 = catName,
                        type                 = 4,
                        position             = cat["position"]?.GetValue<int>() ?? 0,
                        permission_overwrites = overwrites,
                    }, ct);

                    channelIdMap[catId] = newCat["id"]!.GetValue<string>();
                    Log($"  Category \"{catName}\" cloned", LogLevel.Success);
                }
                catch (Exception ex)
                {
                    Log($"  Category \"{catName}\" failed: {ex.Message}", LogLevel.Warn);
                }

                await Task.Delay(400, ct);
            }

            var channels = srcChannels
                .Where(c => c?["type"]?.GetValue<int>() != 4)
                .OrderBy(c => c?["position"]?.GetValue<int>() ?? 0)
                .ToList();

            foreach (var ch in channels)
            {
                if (ch is null) continue;
                var chId   = ch["id"]!.GetValue<string>();
                var chName = ch["name"]?.GetValue<string>() ?? "channel";
                var chType = ch["type"]?.GetValue<int>() ?? 0;

                if (options.SkipTickets && IsTicketChannel(chName))
                {
                    Log($"  Skipped ticket channel \"{chName}\"", LogLevel.Warn);
                    continue;
                }

                string? newParentId = null;
                var oldParentId = ch["parent_id"]?.GetValue<string>();
                if (oldParentId is not null && channelIdMap.TryGetValue(oldParentId, out var mapped))
                    newParentId = mapped;

                var overwrites = BuildOverwriteList(ch["permission_overwrites"] as JsonArray, roleIdMap);

                try
                {
                    var body = new Dictionary<string, object?>
                    {
                        ["name"]                 = chName,
                        ["type"]                 = chType,
                        ["position"]             = ch["position"]?.GetValue<int>() ?? 0,
                        ["permission_overwrites"] = overwrites,
                    };

                    if (newParentId is not null)
                        body["parent_id"] = newParentId;

                    if (chType == 0 || chType == 5)
                    {
                        var topic = ch["topic"]?.GetValue<string>();
                        if (topic is not null) body["topic"] = topic;
                        body["nsfw"]               = ch["nsfw"]?.GetValue<bool>() ?? false;
                        body["rate_limit_per_user"] = ch["rate_limit_per_user"]?.GetValue<int>() ?? 0;
                    }

                    if (chType == 2)
                    {
                        body["bitrate"]    = Math.Min(ch["bitrate"]?.GetValue<int>() ?? 64000, 96000);
                        body["user_limit"] = ch["user_limit"]?.GetValue<int>() ?? 0;
                    }

                    if (chType == 13)
                    {
                        body["bitrate"] = Math.Min(ch["bitrate"]?.GetValue<int>() ?? 64000, 96000);
                    }

                    var newCh = await _api.CreateChannelAsync(newGuildId, body, ct);
                    channelIdMap[chId] = newCh["id"]!.GetValue<string>();
                    Log($"  Channel \"{chName}\" cloned", LogLevel.Success);
                }
                catch (Exception ex)
                {
                    Log($"  Channel \"{chName}\" failed: {ex.Message}", LogLevel.Warn);
                }

                await Task.Delay(400, ct);
            }
        }

        if (options.CloneEmojis)
        {
            Log("Cloning emojis…");
            var srcEmojis = await _api.GetEmojisAsync(sourceGuildId, ct);

            foreach (var emoji in srcEmojis)
            {
                if (emoji is null) continue;
                var emojiId   = emoji["id"]?.GetValue<string>();
                var emojiName = emoji["name"]?.GetValue<string>() ?? "emoji";
                var animated  = emoji["animated"]?.GetValue<bool>() ?? false;

                if (emojiId is null) continue;

                var ext = animated ? "gif" : "png";
                var url = $"https://cdn.discordapp.com/emojis/{emojiId}.{ext}";
                var dataUri = await _api.DownloadImageAsDataUriAsync(url, ct);
                if (dataUri is null)
                {
                    Log($"  Emoji \"{emojiName}\" download failed", LogLevel.Warn);
                    continue;
                }

                bool emojiDone = false;
                for (int attempt = 1; attempt <= 5 && !emojiDone; attempt++)
                {
                    try
                    {
                        using var emojiCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        emojiCts.CancelAfter(TimeSpan.FromSeconds(20));
                        await _api.CreateEmojiAsync(newGuildId, new
                        {
                            name  = emojiName,
                            image = dataUri,
                        }, emojiCts.Token);
                        Log($"  Emoji \"{emojiName}\" cloned", LogLevel.Success);
                        emojiDone = true;
                    }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    {
                        Log($"  Emoji \"{emojiName}\" timed out, skipping", LogLevel.Warn);
                        break;
                    }
                    catch (Exception ex)
                    {
                        double waitSec = attempt * 3;
                        var m = System.Text.RegularExpressions.Regex.Match(ex.Message, @"""retry_after""\s*:\s*([\d.]+)");
                        if (m.Success && double.TryParse(m.Groups[1].Value,
                            System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var ra))
                            waitSec = ra + 0.5;

                        if (attempt < 5)
                        {
                            Log($"  Emoji \"{emojiName}\" rate-limited, retrying in {waitSec:0}s… (attempt {attempt}/5)", LogLevel.Warn);
                            await Task.Delay(TimeSpan.FromSeconds(waitSec), ct);
                        }
                        else
                        {
                            Log($"  Emoji \"{emojiName}\" failed after 5 attempts: {ex.Message}", LogLevel.Warn);
                        }
                    }
                }

                await Task.Delay(600, ct);
            }

            Log($"Emoji cloning complete.", LogLevel.Success);
        }

        if (options.CloneMessages && channelIdMap.Count > 0 && srcChannels is not null)
        {
            Log("Cloning messages via webhooks…", LogLevel.Warn);
            Log("Note: only messages you can see are cloned.", LogLevel.Warn);

            foreach (var (srcChId, dstChId) in channelIdMap)
            {
                var srcCh  = srcChannels.FirstOrDefault(c => c?["id"]?.GetValue<string>() == srcChId);
                var chType = srcCh?["type"]?.GetValue<int>() ?? -1;
                if (chType != 0 && chType != 5) continue;

                var chName = srcCh?["name"]?.GetValue<string>() ?? srcChId;
                Log($"  #{chName}: fetching messages…");

                // create webhook in destination channel
                JsonNode webhook;
                try   { webhook = await _api.CreateWebhookAsync(dstChId, "Cloner", ct); }
                catch (Exception ex)
                {
                    Log($"  #{chName}: webhook creation failed — {ex.Message}", LogLevel.Warn);
                    continue;
                }

                var whId    = webhook["id"]!.GetValue<string>();
                var whToken = webhook["token"]!.GetValue<string>();

                try
                {
                    var allMessages = new List<JsonNode>();
                    string? before  = null;

                    while (true)
                    {
                        JsonArray page;
                        try   { page = await _api.GetMessagesAsync(srcChId, before, 100, ct); }
                        catch { break; }
                        if (page.Count == 0) break;

                        foreach (var m in page)
                            if (m is not null) allMessages.Add(m);

                        before = page[page.Count - 1]?["id"]?.GetValue<string>();
                        await Task.Delay(300, ct);
                    }

                    allMessages.Reverse();

                    int sent = 0;
                    foreach (var msg in allMessages)
                    {
                        var msgType = msg["type"]?.GetValue<int>() ?? 0;
                        if (msgType != 0 && msgType != 19) continue;

                        var content = msg["content"]?.GetValue<string>() ?? "";

                        var attachments = msg["attachments"] as JsonArray;
                        if (attachments?.Count > 0)
                        {
                            var urls = attachments
                                .Select(a => a?["url"]?.GetValue<string>())
                                .Where(u => u is not null);
                            content = (content + "\n" + string.Join("\n", urls)).Trim();
                        }

                        if (string.IsNullOrWhiteSpace(content)) continue;
                        if (content.Length > 2000) content = content[..1997] + "…";

                        var authorName = msg["author"]?["global_name"]?.GetValue<string>()
                                      ?? msg["author"]?["username"]?.GetValue<string>()
                                      ?? "Unknown";
                        var authorId     = msg["author"]?["id"]?.GetValue<string>();
                        var authorAvatar = msg["author"]?["avatar"]?.GetValue<string>();
                        string? avatarUrl = null;
                        if (authorId is not null && authorAvatar is not null)
                            avatarUrl = $"https://cdn.discordapp.com/avatars/{authorId}/{authorAvatar}.png?size=64";

                        var body = new Dictionary<string, object?> { ["content"] = content, ["username"] = authorName };
                        if (avatarUrl is not null) body["avatar_url"] = avatarUrl;

                        try
                        {
                            await _api.ExecuteWebhookAsync(whId, whToken, body, ct);
                            sent++;
                        }
                        catch (Exception ex)
                        {
                            Log($"  #{chName}: message failed — {ex.Message}", LogLevel.Warn);
                        }

                        await Task.Delay(600, ct);
                    }

                    Log($"  #{chName}: {sent} message(s) cloned", LogLevel.Success);
                }
                finally
                {
                    try { await _api.DeleteWebhookAsync(whId, whToken, ct); }
                    catch { }
                }
            }

            Log("Message cloning complete.", LogLevel.Success);
        }

        Log("Applying server settings…");
        try
        {
            var fields = new Dictionary<string, object?>
            {
                ["name"]                          = srcName.Length > 100 ? srcName[..100] : srcName,
                ["default_message_notifications"] = srcGuild["default_message_notifications"]?.GetValue<int>() ?? 0,
                ["explicit_content_filter"]       = srcGuild["explicit_content_filter"]?.GetValue<int>() ?? 0,
                ["verification_level"]            = srcGuild["verification_level"]?.GetValue<int>() ?? 0,
            };

            if (iconDataUri is not null)   fields["icon"]   = iconDataUri;
            if (bannerDataUri is not null)  fields["banner"] = bannerDataUri;
            if (splashDataUri is not null)  fields["splash"] = splashDataUri;

            await _api.PatchGuildAsync(newGuildId, fields, ct);
            Log("Server settings applied.", LogLevel.Success);
        }
        catch (Exception ex)
        {
            Log($"Server settings: {ex.Message}", LogLevel.Warn);
        }

        Log($"All done! Server ID: {newGuildId}", LogLevel.Success);
        return newGuildId;
    }

    private static List<object> BuildOverwriteList(JsonArray? srcOverwrites,
        Dictionary<string, string> roleIdMap)
    {
        var result = new List<object>();
        if (srcOverwrites is null) return result;

        foreach (var ow in srcOverwrites)
        {
            if (ow is null) continue;
            var oldId = ow["id"]?.GetValue<string>();
            var type  = ow["type"]?.GetValue<int>() ?? 0;
            if (oldId is null) continue;

            if (type == 0)
            {
                var newId = oldId;
                if (!roleIdMap.TryGetValue(oldId, out newId!))
                    newId = oldId;
                result.Add(new
                {
                    id    = newId,
                    type  = 0,
                    allow = ow["allow"]?.GetValue<string>() ?? "0",
                    deny  = ow["deny"]?.GetValue<string>()  ?? "0",
                });
            }
        }

        return result;
    }

    private void Log(string msg, LogLevel level = LogLevel.Info)
        => _log(msg, level);
}

public sealed class ClonerOptions
{
    public bool    CloneRoles    { get; set; } = true;
    public bool    CloneChannels { get; set; } = true;
    public bool    CloneEmojis   { get; set; } = true;
    public bool    CloneIcon     { get; set; } = true;
    public bool    CloneMessages { get; set; } = false;
    public bool    SkipTickets   { get; set; } = false;
    /// <summary>If set, clone into this existing guild instead of creating a new one.</summary>
    public string? TargetGuildId { get; set; } = null;
}
