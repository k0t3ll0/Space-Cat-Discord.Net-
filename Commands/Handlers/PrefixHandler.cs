using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Space_Cat_v3.Commands.Handlers
{
    public class PrefixHandler
    {

        private readonly DiscordSocketClient _client;
        private readonly CommandService _commands;
        private readonly IConfigurationRoot _config;


        // Retrieve client and CommandService instance via ctor
        public PrefixHandler(IServiceProvider service, IConfigurationRoot config)
        {
            _commands = service.GetRequiredService<CommandService>();
            _client = service.GetRequiredService<DiscordSocketClient>();
            _config = config;
        }

        public async Task InitializeAsync()
        {
            _client.MessageReceived += HandleCommandAsync;
            await Task.CompletedTask;
        }
        public void AddModule<T>()
        {
            _commands.AddModuleAsync<T>(null);
        }

        private Task HandleCommandAsync(SocketMessage messageParam)
        {
            _ = Task.Run(async () =>
                {
                    // Don't process the command if it was a system message
                    var message = messageParam as SocketUserMessage;
                    if (message == null) return;
                    // Create a number to track where the prefix ends and the command begins
                    int argPos = 0;
                    SocketGuildUser? socketGuildUser = message.Author as SocketGuildUser;
                    //manage_message = socketGuildUser.GuildPermissions.ViewAuditLog;
                    // Determine if the message is a command based on the prefix and make sure no bots trigger commands
                    if (!(message.HasCharPrefix(_config["prefix"][0], ref argPos) || message.HasMentionPrefix(_client.CurrentUser, ref argPos)) || message.Author.IsBot)
                        return;

                    // Create a WebSocket-based command context based on the message
                    var context = new SocketCommandContext(_client, message);

                    // Execute the command with the command context we just
                    // created, along with the service provider for precondition checks.
                    await _commands.ExecuteAsync(
                        context: context,
                        argPos: argPos,
                        services: null);
                });
            return Task.CompletedTask;
        }
    }
}

