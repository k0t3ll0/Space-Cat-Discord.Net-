using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Discord.Interactions;
using Space_Cat_v3.Commands.Handlers;
using Victoria;


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

           
            
            //Создаём билдер для сервисов
            using IHost host = Host.CreateDefaultBuilder()
                .ConfigureServices((_, services) =>
                    services
                    .AddSingleton(config)
                    // Add the DiscordSocketClient, along with specifying the GatewayIntents and user caching
                    .AddSingleton(x => new DiscordSocketClient(new DiscordSocketConfig
                    {
                        UseInteractionSnowflakeDate = false,
                        GatewayIntents = GatewayIntents.All,
                        AlwaysDownloadUsers = true,
                    }))
                    .AddSingleton(x => new InteractionService(x.GetRequiredService<DiscordSocketClient>()))
                    .AddSingleton<InteractionHandler>()
                    .AddSingleton(x => new CommandService())
                    .AddSingleton<PrefixHandler>()
                    .AddSingleton(x => new CommandService())
                    .AddSingleton<ReactionRoleService>()
                    .AddSingleton<AudioService>()
                    .AddLavaNode(x=>
                    {
                        x.Hostname = "127.0.0.1";
                        x.Port = 2333;
                        x.Authorization = "youshallnotpass";
                        x.Version = 4;
                        x.SelfDeaf = true;
                        x.EnableResume = true;
                        x.SocketConfiguration = new()
                        {
                            BufferSize = 8144,
                            ReconnectAttempts = -1,
                            ReconnectDelay = 1000
                        };
                    })  
                    .AddSingleton<SimpleAutoRoleService>()
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
            await pCommands.InitializeAsync();

            var rCommands = provider.GetRequiredService<ReactionRoleService>();
            await rCommands.InitializeAsync();

            var aCommands = provider.GetRequiredService<SimpleAutoRoleService>();   

            List<ulong> ids = config["Discord:Guild"].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(ulong.Parse).ToList();

            _client.Ready += async() => 
            {
                /*for (int i = 0; i < ids.Count; i++)
                    await sCommands.RegisterCommandsToGuildAsync(ids[i]).ConfigureAwait(false);*/
                await sCommands.RegisterCommandsGloballyAsync();
                await provider.UseLavaNodeAsync();
                await Task.CompletedTask;
                await _client.SetGameAsync("Команды бота: !help", null, ActivityType.Playing);
            };

            await _client.LoginAsync(TokenType.Bot, config["Discord:tokens:discord"]);
            await _client.StartAsync();

            await Task.Delay(-1);
        }
    }
}

