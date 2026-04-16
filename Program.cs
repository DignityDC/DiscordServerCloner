using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace DiscordServerCloner;

static class Program
{
    [DllImport("kernel32.dll")] static extern nint GetStdHandle(int n);
    [DllImport("kernel32.dll")] static extern bool GetConsoleMode(nint h, out uint m);
    [DllImport("kernel32.dll")] static extern bool SetConsoleMode(nint h, uint m);

    static string Rgb(int r, int g, int b, string text) =>
        $"\x1b[38;2;{r};{g};{b}m{text}\x1b[0m";
    static string BgRgb(int r, int g, int b, string text) =>
        $"\x1b[48;2;{r};{g};{b}m{text}\x1b[0m";
    static string Bold(string text)  => $"\x1b[1m{text}\x1b[22m";
    static string Dim(string text)   => $"\x1b[2m{text}\x1b[22m";

    static readonly (int r,int g,int b) CBlurple = (88,  101, 242);
    static readonly (int r,int g,int b) CPurple  = (114,  80, 220);
    static readonly (int r,int g,int b) CSuccess = (35,  165,  89);
    static readonly (int r,int g,int b) CWarn    = (240, 178,  50);
    static readonly (int r,int g,int b) CError   = (242,  63,  67);
    static readonly (int r,int g,int b) CMuted   = (181, 186, 193);

    static string Blurple(string s) => Rgb(CBlurple.r, CBlurple.g, CBlurple.b, s);
    static string Purple(string s)  => Rgb(CPurple.r,  CPurple.g,  CPurple.b,  s);
    static string Muted(string s)   => Rgb(CMuted.r,   CMuted.g,   CMuted.b,   s);
    static string Ok(string s)      => Rgb(CSuccess.r, CSuccess.g, CSuccess.b, s);
    static string Warn(string s)    => Rgb(CWarn.r,    CWarn.g,    CWarn.b,    s);
    static string Err(string s)     => Rgb(CError.r,   CError.g,   CError.b,   s);

    static void PrintBanner()
    {
        Console.WriteLine();
        string[] lines =
        {
            @"   ____  _                       _  _____ _                     ",
            @"  |  _ \(_)___  ___ ___  _ __ __| |/ ____| | ___  _ __   ___ _ __",
            @"  | | | | / __|/ __/ _ \| '__/ _` | |    | |/ _ \| '_ \ / _ \ '__|",
            @"  | |_| | \__ \ (_| (_) | | | (_| | |____| | (_) | | | |  __/ |  ",
            @"  |____/|_|___/\___\___/|_|  \__,_|\_____|_|\___/|_| |_|\___|_|  ",
        };

        for (int li = 0; li < lines.Length; li++)
        {
            string line = lines[li];
            var sb = new System.Text.StringBuilder();
            for (int ci = 0; ci < line.Length; ci++)
            {
                float t = (float)ci / Math.Max(line.Length - 1, 1);
                int r = (int)(CBlurple.r + t * (CPurple.r - CBlurple.r));
                int g = (int)(CBlurple.g + t * (CPurple.g - CBlurple.g));
                int b = (int)(CBlurple.b + t * (CPurple.b - CBlurple.b));
                sb.Append($"\x1b[38;2;{r};{g};{b}m{line[ci]}");
            }
            sb.Append("\x1b[0m");
            Console.WriteLine(sb.ToString());
        }

        Console.WriteLine();
        Console.WriteLine("  " + Muted("─────────────────────────────────────────────────────────────"));
        Console.WriteLine("  " + Blurple(Bold("Discord Server Cloner")) + "  " +
                          Muted("v1.0  ·  github.com/DignityDC/DiscordServerCloner"));
        Console.WriteLine("  " + Muted("─────────────────────────────────────────────────────────────"));
        Console.WriteLine();
    }

    static string Ask(string label, string hint = "", bool secret = false)
    {
        Console.Write("  " + Blurple("❯ ") + Bold(label));
        if (hint != "") Console.Write("  " + Dim(Muted(hint)));
        Console.Write("\n  " + Muted("  › "));

        string value;
        if (secret)
        {
            var buf = new System.Text.StringBuilder();
            ConsoleKeyInfo key;
            while ((key = Console.ReadKey(true)).Key != ConsoleKey.Enter)
            {
                if (key.Key == ConsoleKey.Backspace && buf.Length > 0)
                {
                    buf.Remove(buf.Length - 1, 1);
                    Console.Write("\b \b");
                }
                else if (key.KeyChar >= ' ')
                {
                    buf.Append(key.KeyChar);
                    Console.Write(Muted("•"));
                }
            }
            value = buf.ToString();
            Console.WriteLine();
        }
        else
        {
            value = Console.ReadLine() ?? "";
        }
        return value.Trim();
    }

    static bool AskBool(string label, bool defaultYes = true)
    {
        string hint = defaultYes ? "[Y/n]" : "[y/N]";
        Console.Write("  " + Purple("  ") + Muted(label) + "  " + Dim(Muted(hint)) + "  ");
        var line = Console.ReadLine()?.Trim().ToLower() ?? "";
        if (line == "") return defaultYes;
        return line.StartsWith('y');
    }

    static void Log(string msg, string level = "info")
    {
        var ts = Muted($"[{DateTime.Now:HH:mm:ss}]");
        var (icon, colored) = level switch
        {
            "ok"   => (Ok("  ✓ "),   Ok(msg)),
            "warn" => (Warn("  ! "),  Warn(msg)),
            "err"  => (Err("  ✗ "),  Err(msg)),
            _      => (Muted("  · "), msg),
        };
        Console.WriteLine($"  {ts} {icon}{colored}");
    }

    static void Section(string title)
    {
        Console.WriteLine();
        Console.WriteLine("  " + Blurple("┌─ ") + Bold(Blurple(title)));
    }

    static async Task Main()
    {
        var hOut = GetStdHandle(-11);
        if (GetConsoleMode(hOut, out uint mode))
            SetConsoleMode(hOut, mode | 0x0004);
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        PrintBanner();

        Section("Authentication");
        var token = Ask("User Token", "Ctrl Shift + I > Application > Storage > Local Storage > token", secret: true);
        if (string.IsNullOrEmpty(token)) { Console.WriteLine(Err("  No token provided.")); return; }

        Section("Server");
        var sourceId = Ask("Source Server ID", "(the server to clone FROM)");
        if (string.IsNullOrEmpty(sourceId)) { Console.WriteLine(Err("  No source ID.")); return; }

        var targetId = Ask("Target Server ID", "(leave blank to create a new server)");

        Section("Options");
        var cloneRoles    = AskBool("Clone roles?");
        var cloneChannels = AskBool("Clone channels?");
        var cloneEmojis   = AskBool("Clone emojis?");
        var cloneIcon     = AskBool("Clone icon & banner?");
        var skipTickets   = AskBool("Skip ticket channels?", defaultYes: false);
        Console.WriteLine();
        Console.WriteLine("  " + Warn("  ! ") + Warn("Message cloning sends messages as webhooks using your account."));
        Console.WriteLine("  " + Warn("  ! ") + Warn("Only messages you can actually see in the source server are cloned."));
        Console.WriteLine("  " + Warn("  ! ") + Warn("This will take a very long time on servers with many messages."));
        var cloneMessages = AskBool("Clone messages? (slow — read warnings above)", defaultYes: false);

        Console.WriteLine();
        Console.WriteLine("  " + Muted("─────────────────────────────────────────────────────────────"));
        Console.Write("  " + Blurple("  Press ENTER to start cloning, or Ctrl+C to abort… "));
        Console.ReadLine();
        Console.WriteLine("  " + Muted("─────────────────────────────────────────────────────────────"));
        Console.WriteLine();

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            using var api = new DiscordApi(token);

            try
            {
                var me  = await api.GetCurrentUserAsync(cts.Token);
                var tag = $"{me["username"]?.GetValue<string>()}#{me["discriminator"]?.GetValue<string>() ?? "0"}";
                Log($"Logged in as {tag}", "ok");
            }
            catch
            {
                Log("Invalid token or could not reach Discord.", "err");
                return;
            }

            var opts = new ClonerOptions
            {
                CloneRoles    = cloneRoles,
                CloneChannels = cloneChannels,
                CloneEmojis   = cloneEmojis,
                CloneIcon     = cloneIcon,
                CloneMessages = cloneMessages,
                SkipTickets   = skipTickets,
                TargetGuildId = string.IsNullOrWhiteSpace(targetId) ? null : targetId,
            };

            var svc = new ClonerService(api, (msg, lvl) =>
            {
                var l = lvl switch
                {
                    ClonerService.LogLevel.Success => "ok",
                    ClonerService.LogLevel.Warn    => "warn",
                    ClonerService.LogLevel.Error   => "err",
                    _                              => "info",
                };
                Log(msg, l);
            });

            var newId = await svc.CloneAsync(sourceId, opts, cts.Token);

            Console.WriteLine();
            Console.WriteLine("  " + Muted("─────────────────────────────────────────────────────────────"));
            Console.WriteLine("  " + Ok(Bold("  Clone complete!")) + "  " + Muted("New server ID:") + "  " + Blurple(Bold(newId)));
            Console.WriteLine("  " + Muted("─────────────────────────────────────────────────────────────"));
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine();
            Log("Cancelled by user.", "warn");
        }
        catch (Exception ex)
        {
            Log(ex.Message, "err");
        }

        Console.WriteLine();
        Console.Write(Muted("  Press any key to exit… "));
        Console.ReadKey(true);
        Console.WriteLine();
    }
}
