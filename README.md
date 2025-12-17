# Super Recruiter

A .NET Worker Service that polls WoWProgress.com for players looking for guilds and sends notifications to a Discord webhook.

## Features

- **Automated Polling**: Checks WoWProgress every 5 minutes (configurable) for new players
- **Player Tracking**: Maintains a cache of seen players to only notify about new entries
- **Discord Integration**: Mock implementation ready for Discord webhook notifications
- **HTML Parsing**: Uses HtmlAgilityPack to parse player data including:
  - Race and Class
  - Guild affiliation
  - Realm
  - Item Level
  - Last updated timestamp
- **Test Mode**: Use sample data for testing when live site blocks requests

## Quick Start

1. **Clone and build**:

   ```bash
   dotnet restore
   dotnet build
   ```

2. **Run with test data** (recommended to verify setup):

   ```bash
   dotnet run
   ```

   The app will use sample data by default (see `UseTestData` in appsettings.json)

3. **Configure for live use**:
   - Set `UseTestData: false` in appsettings.json
   - Add your Discord webhook URL (optional)

## Configuration

Edit `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "PollingIntervalMinutes": 5,
  "UseTestData": true,
  "Discord": {
    "WebhookUrl": "https://discord.com/api/webhooks/YOUR_WEBHOOK_URL"
  }
}
```

### Configuration Options

- **PollingIntervalMinutes**: How often to check for new players (default: 5 minutes)
- **UseTestData**: Set to `true` to use sample data instead of live site (useful for testing)
- **Discord:WebhookUrl**: Your Discord webhook URL (leave empty to use mock mode)

## Running the Application

```bash
dotnet run
```

Expected output:

```
info: super_recruiter.Worker[0]
      Super Recruiter worker starting. Polling interval: 5 minutes
warn: super_recruiter.Services.WowProgressService[0]
      Using TEST DATA mode - not fetching from live site
info: super_recruiter.Services.WowProgressService[0]
      Successfully parsed 3 players
info: super_recruiter.Worker[0]
      Found 3 new player(s) out of 3 total
info: super_recruiter.Services.DiscordWebhookService[0]
      === MOCK DISCORD WEBHOOK ===
```

## Discord Webhook Setup

To enable actual Discord notifications:

1. Create a webhook in your Discord server (Server Settings → Integrations → Webhooks)
2. Copy the webhook URL
3. Add it to `appsettings.json` under `Discord:WebhookUrl`
4. Uncomment the Discord sending code in [Services/DiscordWebhookService.cs](Services/DiscordWebhookService.cs)

The webhook will send rich embeds with:

- Player race and class
- Item level
- Realm and guild
- Character link

## Known Limitations

⚠️ **Bot Protection**: WoWProgress.com uses Cloudflare or similar bot protection that blocks automated requests (HTTP 403).

### Current Workarounds:

1. **Test Mode** (included): Set `UseTestData: true` to test the application with sample data
2. **Manual Proxy**: Use a proxy service or VPN
3. **Browser Automation** (recommended): Implement Selenium or Playwright

### Implementing Browser Automation (Playwright)

For production use, consider using Playwright to bypass bot protection:

```bash
dotnet add package Microsoft.Playwright
pwsh bin/Debug/net10.0/playwright.ps1 install
```

Then modify [Services/WowProgressService.cs](Services/WowProgressService.cs) to use Playwright instead of HttpClient:

```csharp
using Microsoft.Playwright;

// In GetLookingForGuildPlayersAsync:
var playwright = await Playwright.CreateAsync();
var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
var page = await browser.NewPageAsync();
await page.GotoAsync(BaseUrl + LfgUrl);
var html = await page.ContentAsync();
```

## Project Structure

```
super-recruiter/
├── Models/
│   └── Player.cs                      # Player data model
├── Services/
│   ├── WowProgressService.cs          # Web scraping service
│   ├── DiscordWebhookService.cs       # Discord notification service
│   └── TestDataProvider.cs            # Sample data for testing
├── Worker.cs                          # Main background worker
├── Program.cs                         # Service configuration & DI
└── appsettings.json                   # Configuration
```

## How It Works

1. **Worker** runs on a configurable interval (default: 5 minutes)
2. **WowProgressService** fetches and parses the LFG page
3. **Player tracking** compares against previously seen players
4. **DiscordWebhookService** notifies about new players
5. **Cache cleanup** removes old entries when cache exceeds 1000 players

## Future Enhancements

- [ ] Add Playwright/Selenium support for bot protection bypass
- [ ] Implement database persistence for player tracking
- [ ] Add filtering options (class, realm, item level thresholds)
- [ ] Support multiple regions (US, EU, KR, TW, CN)
- [ ] Add health checks and monitoring endpoints
- [ ] Implement rate limiting and retry policies
- [ ] Web dashboard for viewing tracked players
- [ ] Multiple webhook support for different Discord servers

## Troubleshooting

**403 Forbidden Error**:

- Set `UseTestData: true` to verify the app works
- Consider implementing Playwright/Selenium
- Try running from a different IP/network

**No Players Found**:

- Check if the site structure changed
- Verify the XPath selector in WowProgressService.cs
- Enable debug logging in appsettings.json

**Discord Not Working**:

- Verify webhook URL is correct
- Uncomment the actual sending code in DiscordWebhookService.cs
- Check Discord server permissions
