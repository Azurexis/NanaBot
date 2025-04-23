using System;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using Discord;
using Discord.WebSocket;
using Discord.Webhook;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Text.RegularExpressions;

namespace NanaBot
{
    class Program
    {
        //Variables
        private static Task Main(string[] args) => new Program().MainAsync();

        private IConfigurationRoot configuration = null!;

        private DiscordSocketClient discordSocketClient = null!;
        private string discordToken = null!;
        private ulong discordNanaChannelID;
        private ulong discordGamelogChannelID;

        public async Task MainAsync()
        {
            //Set configuration
            configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();

            //Set configuration variables
            discordToken = configuration["DISCORD_TOKEN"];
            discordNanaChannelID = ulong.Parse(configuration["NANA_CHANNEL_ID"]);
            discordGamelogChannelID = ulong.Parse(configuration["GAMELOG_CHANNEL_ID"]);

            // Initialize Discord client with necessary intents
            discordSocketClient = new DiscordSocketClient(new DiscordSocketConfig
            {
                GatewayIntents = GatewayIntents.Guilds
                               | GatewayIntents.GuildMessages
                               | GatewayIntents.MessageContent
            });

            //Initialize discord socket client
            discordSocketClient.MessageReceived += Discord_HandleMessageAsync;

            await discordSocketClient.LoginAsync(TokenType.Bot, discordToken);
            await discordSocketClient.StartAsync();

            //Start listener
            StartListener();

            //Console
            Console.WriteLine("[Debug] NanaBot successfully set up!");

            //Loop
            await Task.Delay(Timeout.Infinite);
        }

        private async Task Discord_HandleMessageAsync(SocketMessage _message)
        {
            //Don't process own messages
            if (_message.Author.Id == discordSocketClient.CurrentUser.Id)
                return;

            //Don't process webhooks
            if (_message.Source == MessageSource.Webhook)
                return;

            //Don't process messages in non-text channels
            if (_message.Channel is not SocketTextChannel textChannel)
                return;

            //Don't process messages not in the right channel ID
            if (textChannel.Id != discordNanaChannelID)
                return;

            // Don't process single-word "What?" messages
            if (_message.Content.Trim().Equals("What?", StringComparison.OrdinalIgnoreCase))
                return;

            //Regex: Preserve Nana emojis and specific punctuation (. , ! ?), replace everything else with "Nana"
            string regexPattern = @"(<a?:nana\w*:\d+>)|(:nana\w*?:)|([\.,!\?])|(\S+)";

            string modifiedMessage = Regex.Replace(_message.Content, regexPattern, match =>
            {
                if (match.Groups[1].Success)
                    return match.Value;
                if (match.Groups[2].Success)
                    return match.Value;
                if (match.Groups[3].Success)
                    return match.Value;

                return "Nana";
            });

            //Delete original message
            try
            {
                await _message.DeleteAsync();

                Console.WriteLine("[Debug] Deleted original message (" + _message.Content + ").");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[Debug] Delete failed: " + ex.Message);

                return;
            }

            //Set webhook
            var webhooks = await textChannel.GetWebhooksAsync();
            var webhookInfo = webhooks.FirstOrDefault(w => w.Name == "NanaWebhook")
                              ?? await textChannel.CreateWebhookAsync("NanaWebhook");

            //Send modified message via webhook
            var webhookClient = new DiscordWebhookClient(webhookInfo.Id, webhookInfo.Token!);
            await webhookClient.SendMessageAsync(
                modifiedMessage,
                username: _message.Author.Username,
                avatarUrl: _message.Author.GetAvatarUrl() ?? _message.Author.GetDefaultAvatarUrl()
            );
        }

        private void StartListener()
        {
            //Prepare variables
            var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
            var webhookSecret = Environment.GetEnvironmentVariable("SECRET");

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls($"http://*:{port}");

            var app = builder.Build();

            //Process echo
            app.MapPost("/", async ctx =>
            {
                //Check secret
                var auth = ctx.Request.Headers["Authorization"].FirstOrDefault();

                if (string.IsNullOrEmpty(webhookSecret) || auth != webhookSecret)
                {
                    ctx.Response.StatusCode = 401;
                    return;
                }

                //Read message
                using var reader = new StreamReader(ctx.Request.Body);

                var content = await reader.ReadToEndAsync();

                //Post to Discord
                if (!string.IsNullOrWhiteSpace(content))
                {
                    var channel = discordSocketClient.GetChannel(discordGamelogChannelID)
                                      as IMessageChannel;

                    if (channel != null)
                        await channel.SendMessageAsync(content.Trim());
                }
            });

            //Run kestrel
            _ = Task.Run(() => app.RunAsync());
        }
    }
}
