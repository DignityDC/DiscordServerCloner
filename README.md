# Discord Server Cloner

Clones a Discord server into a new one (or an existing one you own). Copies roles, channels, emojis, icon, banner, and optionally messages — all from your account, no bot required.

---

## Requirements

- Windows 10/11
- A Discord account with access to the source server
- The compiled `.exe` (see below), or .NET 8 SDK if building from source

---

## Getting your token

1. Open Discord in your browser (discord.com)
2. Press `Ctrl + Shift + I` to open DevTools
3. Go to **Application** → **Storage** → **Local Storage** → `https://discord.com`
4. Find the key called `token` — the value is your user token

> Keep this private. Anyone with it has full access to your account.

---

## Getting server IDs

1. In Discord, go to **User Settings** → **Advanced** → enable **Developer Mode**
2. Right-click any server icon → **Copy Server ID**

---

## Building

```
dotnet publish -c Release -o publish
```

The exe will be at `publish\DiscordServerCloner.exe`. It's self-contained, no install needed.

---

## Usage

Just run the exe. It'll walk you through everything:

```
❯ User Token       (masked input)
❯ Source Server ID (the server to clone FROM)
❯ Target Server ID (leave blank to create a new server)

Options:
  Clone roles?              [Y/n]
  Clone channels?           [Y/n]
  Clone emojis?             [Y/n]
  Clone icon & banner?      [Y/n]
  Skip ticket channels?     [y/N]
  Clone messages? (slow)    [y/N]
```

Hit Enter to start. Press `Ctrl+C` at any time to cancel cleanly.

---

## What gets cloned

| Thing | Notes |
|---|---|
| Roles | Colors, permissions, hoist, mentionable. Order is preserved. |
| Categories | With permission overwrites mapped to new roles. |
| Channels | Text, voice, stage, announcement. Topics, bitrate, slowmode, NSFW flag. |
| Emojis | Static and animated. Rate-limits are handled automatically with retries. |
| Icon / Banner / Splash | Downloaded and re-uploaded. |
| Messages | Via webhooks. Shows original username + avatar. Attachments are linked. |

---

## Notes

- **Message cloning** only copies messages you can actually read in the source server. It paginates the full history per channel so it can take a long time on busy servers.
- Channels with `ticket-`, `needed`, or `handling` in the name are skipped when "Skip ticket channels" is enabled.
- Community-only features (rules channel, discovery, etc.) won't carry over since the new server isn't a community server. That's expected — everything else will still clone fine.
- If the source server has a lot of emojis, expect some waiting. Discord heavily rate-limits emoji creation.
- This uses a **user token**, not a bot token. Use it on accounts you own.
