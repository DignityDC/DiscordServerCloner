using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiscordServerCloner;

public sealed class DiscordApi : IDisposable
{
    private const string Base = "https://discord.com/api/v10";

    private readonly HttpClient _hc;
    private readonly HttpClient _whc;
    private readonly JsonSerializerOptions _jo = new() { PropertyNameCaseInsensitive = true };

    private const string SuperProps =
        "eyJvcyI6IldpbmRvd3MiLCJicm93c2VyIjoiQ2hyb21lIiwiZGV2aWNlIjoiIiwic3lzdGVtX2xvY2FsZSI6ImVuLVVTIiwiYnJvd3Nlcl91c2VyX2FnZW50IjoiTW96aWxsYS81LjAgKFdpbmRvd3MgTlQgMTAuMDsgV2luNjQ7IHg2NCkgQXBwbGVXZWJLaXQvNTM3LjM2IChLSFRNTCwgbGlrZSBHZWNrbykgQ2hyb21lLzEyNC4wLjAuMCBTYWZhcmkvNTM3LjM2IiwiYnJvd3Nlcl92ZXJzaW9uIjoiMTI0LjAuMC4wIiwib3NfdmVyc2lvbiI6IjEwIiwicmVmZXJyZXIiOiIiLCJyZWZlcnJpbmdfZG9tYWluIjoiIiwicmVmZXJyZXJfY3VycmVudCI6IiIsInJlZmVycmluZ19kb21haW5fY3VycmVudCI6IiIsInJlbGVhc2VfY2hhbm5lbCI6InN0YWJsZSIsImNsaWVudF9idWlsZF9udW1iZXIiOjk5OTksImNsaWVudF9ldmVudF9zb3VyY2UiOm51bGx9";

    public DiscordApi(string token)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = true };
        _hc = new HttpClient(handler);

        _hc.DefaultRequestHeaders.Add("Authorization", token);
        _hc.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        _hc.DefaultRequestHeaders.Add("X-Super-Properties", SuperProps);
        _hc.DefaultRequestHeaders.Add("X-Discord-Locale", "en-US");
        _hc.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
        _hc.DefaultRequestHeaders.Add("Origin", "https://discord.com");
        _hc.DefaultRequestHeaders.Add("Referer", "https://discord.com/channels/@me");
        _hc.Timeout = TimeSpan.FromSeconds(30);

        _whc = new HttpClient();
        _whc.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        _whc.Timeout = TimeSpan.FromSeconds(30);
    }

    private async Task<JsonNode?> SendAsync(HttpMethod method, string path,
        object? body = null, CancellationToken ct = default)
    {
        while (true)
        {
            var req = new HttpRequestMessage(method, Base + path);
            if (body is not null)
            {
                req.Content = new StringContent(
                    JsonSerializer.Serialize(body),
                    Encoding.UTF8, "application/json");
            }

            var resp = await _hc.SendAsync(req, ct);

            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                double wait = 5;
                if (resp.Headers.RetryAfter?.Delta is { } d)
                    wait = d.TotalSeconds + 0.5;
                else if (resp.Headers.RetryAfter?.Date is { } dt)
                    wait = (dt - DateTimeOffset.UtcNow).TotalSeconds + 0.5;
                else
                {
                    try
                    {
                        var rl = await resp.Content.ReadAsStringAsync(ct);
                        var node = JsonNode.Parse(rl);
                        wait = node?["retry_after"]?.GetValue<double>() ?? 5;
                    }
                    catch { }
                }
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(wait, 1)), ct);
                continue;
            }

            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                throw new Exception($"HTTP {(int)resp.StatusCode} {resp.StatusCode} — {path}\n{err}");
            }

            if (resp.StatusCode == HttpStatusCode.NoContent)
                return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            return string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json);
        }
    }

    private async Task<JsonNode?> GetAsync(string path, CancellationToken ct = default)
        => await SendAsync(HttpMethod.Get, path, null, ct);

    private async Task<JsonNode?> PostAsync(string path, object body, CancellationToken ct = default)
        => await SendAsync(HttpMethod.Post, path, body, ct);

    private async Task<JsonNode?> PatchAsync(string path, object body, CancellationToken ct = default)
        => await SendAsync(HttpMethod.Patch, path, body, ct);

    private async Task<JsonNode?> PutAsync(string path, object? body, CancellationToken ct = default)
        => await SendAsync(HttpMethod.Put, path, body, ct);

    private async Task<JsonNode?> DeleteAsync(string path, CancellationToken ct = default)
        => await SendAsync(HttpMethod.Delete, path, null, ct);


    private async Task<JsonNode?> PatchWithIconAsync(string path,
        Dictionary<string, object?> fields, CancellationToken ct)
    {
        while (true)
        {
            var req = new HttpRequestMessage(HttpMethod.Patch, Base + path);
            req.Content = new StringContent(
                JsonSerializer.Serialize(fields),
                Encoding.UTF8, "application/json");

            var resp = await _hc.SendAsync(req, ct);

            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                double wait = 5;
                try
                {
                    var rl = await resp.Content.ReadAsStringAsync(ct);
                    var node = JsonNode.Parse(rl);
                    wait = node?["retry_after"]?.GetValue<double>() ?? 5;
                }
                catch { }
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(wait, 1)), ct);
                continue;
            }

            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                throw new Exception($"HTTP {(int)resp.StatusCode} — {err}");
            }

            var json = await resp.Content.ReadAsStringAsync(ct);
            return string.IsNullOrWhiteSpace(json) ? null : JsonNode.Parse(json);
        }
    }

    public async Task<JsonNode> GetGuildAsync(string guildId, CancellationToken ct = default)
        => (await GetAsync($"/guilds/{guildId}", ct))!;

    public async Task<JsonArray> GetChannelsAsync(string guildId, CancellationToken ct = default)
        => (JsonArray)(await GetAsync($"/guilds/{guildId}/channels", ct))!;

    public async Task<JsonArray> GetRolesAsync(string guildId, CancellationToken ct = default)
        => (JsonArray)(await GetAsync($"/guilds/{guildId}/roles", ct))!;

    public async Task<JsonArray> GetEmojisAsync(string guildId, CancellationToken ct = default)
        => (JsonArray)(await GetAsync($"/guilds/{guildId}/emojis", ct))!;

    public async Task<string?> DownloadImageAsDataUriAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var hc = new HttpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            var bytes = await hc.GetByteArrayAsync(url, cts.Token);
            string mime = bytes.Length >= 3 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46
                ? "image/gif"
                : "image/png";
            return $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }
        catch { return null; }
    }

    public async Task<JsonNode> CreateGuildAsync(string name, string? iconDataUri, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["name"] = name };
        if (iconDataUri is not null) body["icon"] = iconDataUri;
        return (await PostAsync("/guilds", body, ct))!;
    }

    public async Task<JsonNode> PatchGuildAsync(string guildId,
        Dictionary<string, object?> fields, CancellationToken ct = default)
        => (await PatchWithIconAsync($"/guilds/{guildId}", fields, ct))!;

    public async Task DeleteChannelAsync(string channelId, CancellationToken ct = default)
        => await DeleteAsync($"/channels/{channelId}", ct);

    public async Task<JsonNode> CreateRoleAsync(string guildId, object roleBody, CancellationToken ct = default)
        => (await PostAsync($"/guilds/{guildId}/roles", roleBody, ct))!;

    public async Task PatchRoleAsync(string guildId, string roleId, object body, CancellationToken ct = default)
        => await PatchAsync($"/guilds/{guildId}/roles/{roleId}", body, ct);

    public async Task<JsonNode> CreateChannelAsync(string guildId, object body, CancellationToken ct = default)
        => (await PostAsync($"/guilds/{guildId}/channels", body, ct))!;

    public async Task PutPermissionAsync(string channelId, string overwriteId, object body, CancellationToken ct = default)
        => await PutAsync($"/channels/{channelId}/permissions/{overwriteId}", body, ct);

    public async Task<JsonNode?> CreateEmojiAsync(string guildId, object body, CancellationToken ct = default)
        => await PostAsync($"/guilds/{guildId}/emojis", body, ct);

    public async Task<JsonNode> GetCurrentUserAsync(CancellationToken ct = default)
        => (await GetAsync("/users/@me", ct))!;

    public async Task<JsonArray> GetMessagesAsync(string channelId, string? before,
        int limit, CancellationToken ct = default)
    {
        var path = $"/channels/{channelId}/messages?limit={limit}";
        if (before is not null) path += $"&before={before}";
        return (JsonArray)(await GetAsync(path, ct))!;
    }

    public async Task<JsonNode> CreateWebhookAsync(string channelId, string name,
        CancellationToken ct = default)
        => (await PostAsync($"/channels/{channelId}/webhooks", new { name }, ct))!;

    public async Task ExecuteWebhookAsync(string webhookId, string webhookToken,
        object body, CancellationToken ct = default)
    {
        var url = $"{Base}/webhooks/{webhookId}/{webhookToken}?wait=false";
        while (true)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var resp = await _whc.SendAsync(req, ct);

            if (resp.StatusCode == HttpStatusCode.TooManyRequests)
            {
                double wait = 5;
                if (resp.Headers.RetryAfter?.Delta is { } d) wait = d.TotalSeconds + 0.5;
                else
                {
                    try { var rl = await resp.Content.ReadAsStringAsync(ct); wait = JsonNode.Parse(rl)?["retry_after"]?.GetValue<double>() ?? 5; } catch { }
                }
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(wait, 1)), ct);
                continue;
            }

            if (!resp.IsSuccessStatusCode)
            {
                var err = await resp.Content.ReadAsStringAsync(ct);
                throw new Exception($"HTTP {(int)resp.StatusCode} {resp.StatusCode} — /webhooks/{webhookId}/{webhookToken}?wait=false\n{err}");
            }
            return;
        }
    }

    public async Task DeleteWebhookAsync(string webhookId, string webhookToken,
        CancellationToken ct = default)
        => await DeleteAsync($"/webhooks/{webhookId}/{webhookToken}", ct);

    public void Dispose() { _hc.Dispose(); _whc.Dispose(); }
}
