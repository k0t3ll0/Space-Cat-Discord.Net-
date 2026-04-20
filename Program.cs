using Discord;
using Discord.Commands;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Space_Cat_v3.Commands.Handlers;
using System.Diagnostics;
using System.Net.Sockets;
using Victoria;


namespace Space_Cat_v3
{
    internal class Program
    {
        private static Process? _lavaLinkProcess;
        private static bool keepRunning = false;
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
                    .AddLavaNode(x =>
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
                            ReconnectAttempts = 3,
                            ReconnectDelay = 4000
                        };
                    })
                    .AddSingleton<SimpleAutoRoleService>()
                )
                .Build();

            AppDomain.CurrentDomain.ProcessExit += BotStop;
            Console.CancelKeyPress += Console_CancelKeyPress;

            try
            {
                if (!IsLavaLinkStarted())
                    await StartLavalinkAsync();
                keepRunning = true;
            }
            catch (Exception ex)
            {
                Console.Clear();
                Console.WriteLine("Ошибка запуска Lavalink {0}", ex.Message);
                StopLavalink();
            }

            while (keepRunning)
            {
                await RunAsync(host);
            }
        }

        private static void Console_CancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            IsLavaLinkStarted(true);
            keepRunning = false;
            Process.GetCurrentProcess().Kill();
        }

        private static async Task StartLavalinkAsync()
        {
            try
            {
                var lavalinkJarPath = Path.Combine(AppContext.BaseDirectory, "Lavalink\\Lavalink.jar");
                var altPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Lavalink\\Lavalink.jar");

                string? jarPath = null;
                if (File.Exists(lavalinkJarPath))
                {
                    jarPath = lavalinkJarPath;
                }
                else if (File.Exists(altPath))
                {
                    jarPath = altPath;
                }

                if (jarPath == null)
                {
                    throw new FileNotFoundException("Lavalink.jar не найден. Скачайте Lavalink.jar и поместите в папку с приложением.");
                }

                _lavaLinkProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "java",
                        Arguments = $"-jar {jarPath}",
                        WorkingDirectory = Path.GetDirectoryName(jarPath),
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    },
                    EnableRaisingEvents = true
                };

                _lavaLinkProcess.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.WriteLine($"[Lavalink] {e.Data}");
                    }
                };

                _lavaLinkProcess.ErrorDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        Console.WriteLine($"[Lavalink ERROR] {e.Data}");
                    }
                };

                Console.WriteLine($"Запуск Lavalink из: {jarPath}");

                _lavaLinkProcess.Start();
                _lavaLinkProcess.BeginOutputReadLine();
                _lavaLinkProcess.BeginErrorReadLine();

                await Task.Delay(8000);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка запуска Lavalink: {ex.Message}");
                throw;
            }
        }

        private static void StopLavalink()
        {
            if (_lavaLinkProcess is null)
            {
                return;
            }

            try
            {
                if (!_lavaLinkProcess.HasExited)
                {
                    _lavaLinkProcess.Kill();
                    _lavaLinkProcess.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка остановки Lavalink: {ex.Message}");
            }
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

            List<ulong> ids = config["Discord:Guild"]!.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(ulong.Parse).ToList();

            _client.Ready += async () =>
            {
                /*for (int i = 0; i < ids.Count; i++)
                    await sCommands.RegisterCommandsToGuildAsync(ids[i]).ConfigureAwait(false);*/
                await sCommands.RegisterCommandsGloballyAsync();
                await provider.UseLavaNodeAsync();
                await Task.CompletedTask;
                await _client.SetGameAsync("Команды бота: !help (!h)", null, ActivityType.Playing);
            };

            await _client.LoginAsync(TokenType.Bot, config["Discord:tokens:discord"]);
            await _client.StartAsync();

            await Task.Delay(-1);
        }

        static void BotStop(object? sender, EventArgs e)
        {
            IsLavaLinkStarted(true);    
        }
        public static bool IsLavaLinkStarted(bool KillProc = false)
        {
            Process[] processes = Process.GetProcessesByName("java");
            foreach (var item in processes)
            {
                if (item.ProcessName == "java")
                {
                    if (KillProc == true)
                        item.Kill();
                    else
                        return true;
                }
            }
            return false;
        }
    }
}

