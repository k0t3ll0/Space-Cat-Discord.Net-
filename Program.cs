
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Discord.Interactions;
using Space_Cat_v3.Commands.Handlers;
using Space_Cat_v3.Commands.Modules;
using Serilog;
using Serilog.Events;

namespace Space_Cat_v3
{
    internal class Program
    {

        static async Task Main(string[] args)
        {
            //Создаём конфиг через билдер
            var config = new ConfigurationBuilder()
                //установить путь
                .SetBasePath(AppContext.BaseDirectory)
                //путь к конфигу
                .AddYamlFile("Config\\config.yml")
                //создать
                .Build();

            Log.Logger = new LoggerConfiguration()
                   .MinimumLevel.Debug()
                   .Enrich.FromLogContext()
                   .WriteTo.Console()
                   .CreateLogger();

            //Создаём билдер для сервисов
            using IHost host = Host.CreateDefaultBuilder()
                .ConfigureServices((_, services) =>
                    services
                    .AddSingleton(config)
                    // Add the DiscordSocketClient, along with specifying the GatewayIntents and user caching
                    .AddSingleton(x => new DiscordSocketClient(new DiscordSocketConfig
                    {
                        GatewayIntents = GatewayIntents.All,
                        AlwaysDownloadUsers = true,
                    }))
                    .AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>()))
                    .AddSingleton<InteractionHandler>()
                    .AddSingleton(x => new CommandService())
                    .AddSingleton<PrefixHandler>()
                )
                .Build();
            

            await RunAsync(host);
        }

        public static async Task RunAsync(IHost host)
        {
            using IServiceScope serviceScope = host.Services.CreateScope();
            IServiceProvider provider = serviceScope.ServiceProvider;

            var _client = provider.GetRequiredService<DiscordSocketClient>();
            var config = provider.GetRequiredService<IConfigurationRoot>();

            var sCommands = provider.GetRequiredService<InteractionService>();

            await provider.GetRequiredService<InteractionHandler>().InitializeAsync();

            var pCommands = provider.GetRequiredService<PrefixHandler>();
            pCommands.AddModule<PrefixModule>();

            await pCommands.InitializeAsync();

            // Subscribe to client log events
            _client.Log += LogAsync;
            sCommands.Log += LogAsync;
            
            

            _client.Ready += () => {  
                
                sCommands.RegisterCommandsToGuildAsync(ulong.Parse(config["testGuild"]));
                return Task.CompletedTask;
            };

            await _client.LoginAsync(TokenType.Bot, config["tokens:discord"]);
            await _client.StartAsync();

            await Task.Delay(-1);
        }

        private static async Task LogAsync(LogMessage message)
        {
            var severity = message.Severity switch
            {
                LogSeverity.Critical => LogEventLevel.Fatal,
                LogSeverity.Error => LogEventLevel.Error,
                LogSeverity.Warning => LogEventLevel.Warning,
                LogSeverity.Info => LogEventLevel.Information,
                LogSeverity.Verbose => LogEventLevel.Verbose,
                LogSeverity.Debug => LogEventLevel.Debug,
                _ => LogEventLevel.Debug
            };
            Log.Write(severity, message.Exception, "[{Source}] {Message}", message.Source, message.Message);
            await Task.CompletedTask;
        }
    }
}

