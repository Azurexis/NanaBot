using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Discord.Webhook;
using Microsoft.Extensions.Configuration;

namespace NanaBot
{
    class Program
    {
        //Variables
        private DiscordSocketClient _client = null!;
        private IConfigurationRoot _config = null!;
        private string _token = null!;
        private ulong _channelId;

        static Task Main(string[] args) => new Program().MainAsync();

        public async Task MainAsync()
        {
            //Load configuration variables
            _config = new ConfigurationBuilder().AddEnvironmentVariables().Build();

            //Set variables
            _token = _config["DISCORD_TOKEN"]
                ?? throw new InvalidOperationException("Environment variable 'DISCORD_TOKEN' must be set.");

            string channelIdString = _config["TARGET_CHANNEL_ID"]
                ?? throw new InvalidOperationException("TARGET_CHANNEL_ID must be set");

            _channelId = ulong.Parse(channelIdString);

            // Initialize Discord client with necessary intents
            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds
                               | GatewayIntents.GuildMessages
                               | GatewayIntents.MessageContent
            });

            // Log events to console
            _client.Log += msg =>
            {
                Console.WriteLine(msg.ToString());
                return Task.CompletedTask;
            };

            // Hook into incoming messages
            _client.MessageReceived += HandleMessageAsync;

            // Authenticate and connect
            await _client.LoginAsync(TokenType.Bot, _token);
            await _client.StartAsync();

            Console.WriteLine("Bot is ready. Listening for messages...");
            // Keep the program alive
            await Task.Delay(Timeout.Infinite);
        }

        private async Task HandleMessageAsync(SocketMessage message)
        {
            // Skip bot and webhook messages
            if (message.Author.Id == _client.CurrentUser.Id || message.Source == MessageSource.Webhook)
                return;

            // Only process text channels
            if (message.Channel is not SocketTextChannel textChannel)
                return;

            // Filter by channel name
            if (textChannel.Id != _channelId)
                return;

            // Count words
            var words = message.Content.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
                return;

            var newContent = string.Join(' ', Enumerable.Repeat("Nana", words.Length));

            // Delete original message
            try { await message.DeleteAsync(); }
            catch (Exception ex) { Console.WriteLine($"Delete failed: {ex.Message}"); return; }

            // Fetch or create webhook
            var webhooks = await textChannel.GetWebhooksAsync();
            var webhookInfo = webhooks.FirstOrDefault(w => w.Name == "NanaWebhook")
                              ?? await textChannel.CreateWebhookAsync("NanaWebhook");

            // Send transformed message via webhook
            var webhookClient = new DiscordWebhookClient(webhookInfo.Id, webhookInfo.Token!);
            await webhookClient.SendMessageAsync(
                newContent,
                username: message.Author.Username,
                avatarUrl: message.Author.GetAvatarUrl() ?? message.Author.GetDefaultAvatarUrl()
            );
        }
    }
}
