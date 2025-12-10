using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Space_Cat_v3.Commands.Handlers;
public class PrefixHandler : IDisposable
{
    private readonly DiscordSocketClient _client;
    private readonly CommandService _commands;
    private readonly IServiceProvider _services;
    private readonly ILogger<PrefixHandler> _logger;
    private readonly List<char> _prefixes;
    private bool _disposed;
    private readonly SemaphoreSlim _commandSemaphore;

    public PrefixHandler(
        IServiceProvider services,
        IConfiguration config,
        ILogger<PrefixHandler> logger = null)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _client = services.GetRequiredService<DiscordSocketClient>();
        _commands = services.GetRequiredService<CommandService>();
        _logger = logger;

        // Важно: НЕ регистрируем ReactionRoleService здесь, если он не был добавлен в DI
        // _reactionRoleService = services.GetRequiredService<ReactionRoleService>();

        _commandSemaphore = new SemaphoreSlim(1, 1);

        // Получаем префиксы из конфигурации
        var prefixConfig = config["prefix"] ?? "!";
        _prefixes = ParsePrefixes(prefixConfig);

        if (_prefixes.Count == 0)
        {
            _prefixes.Add('!');
        }

        _logger?.LogInformation("Инициализирован PrefixHandler с префиксами: {Prefixes}",
            string.Join(", ", _prefixes.Select(p => $"'{p}'")));
    }

    public async Task InitializeAsync()
    {
        try
        {
            _logger?.LogInformation("Начинаем инициализацию PrefixHandler...");

            // Регистрируем все модули из текущей сборки
            await RegisterModulesFromAssemblyAsync(Assembly.GetEntryAssembly());

            // ИЛИ регистрируем конкретные модули
            //await RegisterModuleAsync<ReactionRoleModule>();

            // Подписываемся на события
            _client.MessageReceived += HandleMessageAsync;
            _commands.CommandExecuted += OnCommandExecutedAsync;
            _commands.Log += OnCommandServiceLogAsync;

            _logger?.LogInformation("PrefixHandler инициализирован успешно");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ошибка при инициализации PrefixHandler");
            throw;
        }
    }

    // Регистрация модуля по типу
    public async Task RegisterModuleAsync<T>() where T : class, IModuleBase
    {
        try
        {
            await _commands.AddModuleAsync<T>(_services);
            _logger?.LogInformation("✅ Модуль {ModuleName} зарегистрирован", typeof(T).Name);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ Ошибка при регистрации модуля {ModuleName}", typeof(T).Name);
        }
    }

    // Регистрация всех модулей из сборки
    private async Task RegisterModulesFromAssemblyAsync(Assembly assembly)
    {
        try
        {
            var modules = await _commands.AddModulesAsync(assembly, _services);
            _logger?.LogInformation("✅ Зарегистрировано {Count} модулей из сборки {Assembly}",
                modules.Count(), assembly.GetName().Name);

            // Логируем все зарегистрированные команды
            foreach (var module in _commands.Modules)
            {
                _logger?.LogInformation("Модуль: {ModuleName} ({Commands} команд)",
                    module.Name, module.Commands.Count);

                foreach (var command in module.Commands)
                {
                    _logger?.LogInformation("  Команда: {CommandName} - {Summary}",
                        command.Name, command.Summary);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ошибка при регистрации модулей из сборки");
        }
    }

    // Основной обработчик сообщений
    private async Task HandleMessageAsync(SocketMessage messageParam)
    {
        if (!ShouldProcessMessage(messageParam))
            return;

        var message = (SocketUserMessage)messageParam;
        var context = new SocketCommandContext(_client, message);

        try
        {
            await _commandSemaphore.WaitAsync();

            foreach (var prefix in _prefixes)
            {
                int argPos = 0;

                if (message.HasCharPrefix(prefix, ref argPos) ||
                    message.HasMentionPrefix(_client.CurrentUser, ref argPos))
                {
                    _logger?.LogDebug("Найдена команда: {Content}", message.Content);
                    await ExecuteCommandAsync(context, argPos);
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ошибка при обработке сообщения от {User}", message.Author);

            try
            {
                var errorEmbed = new EmbedBuilder()
                    .WithColor(Color.Red)
                    .WithDescription("❌ Ошибка выполнения команды")
                    .Build();

                await message.Channel.SendMessageAsync(embed: errorEmbed);
            }
            catch { }
        }
        finally
        {
            _commandSemaphore.Release();
        }
    }

    // Выполнение команды
    private async Task ExecuteCommandAsync(SocketCommandContext context, int argPos)
    {
        _logger?.LogDebug("Выполнение команды от {User}: {Message}",
            context.User.Username, context.Message.Content);

        var result = await _commands.ExecuteAsync(context, argPos, _services);

        if (!result.IsSuccess && result.Error != CommandError.UnknownCommand)
        {
            _logger?.LogWarning("Команда не выполнена: {Error} - {Reason}",
                result.Error, result.ErrorReason);

            // Отправляем сообщение об ошибке
            if (result.Error == CommandError.UnmetPrecondition)
            {
                await context.Channel.SendMessageAsync($"🚫 {result.ErrorReason}");
            }
        }
    }

    // Проверка сообщения
    private bool ShouldProcessMessage(SocketMessage message)
    {
        if (message is not SocketUserMessage userMessage)
            return false;

        if (userMessage.Author.IsBot || userMessage.Author.IsWebhook)
            return false;

        if (string.IsNullOrWhiteSpace(userMessage.Content))
            return false;

        return true;
    }

    // Парсинг префиксов
    private List<char> ParsePrefixes(string prefixConfig)
    {
        var prefixes = new List<char>();

        if (!string.IsNullOrWhiteSpace(prefixConfig))
        {
            foreach (var prefix in prefixConfig.Split(','))
            {
                var trimmed = prefix.Trim();
                if (trimmed.Length > 0)
                {
                    prefixes.Add(trimmed[0]);
                }
            }
        }

        return prefixes;
    }

    // Обработчик результатов команд
    private async Task OnCommandExecutedAsync(Optional<CommandInfo> command, ICommandContext context, IResult result)
    {
        if (!result.IsSuccess)
        {
            _logger?.LogWarning("Ошибка команды: {Error} - {Reason}",
                result.Error, result.ErrorReason);
            
        }
    }

    // Логирование CommandService
    private Task OnCommandServiceLogAsync(LogMessage logMessage)
    {
        var logLevel = logMessage.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            LogSeverity.Debug => LogLevel.Trace,
            _ => LogLevel.Information
        };

        _logger?.Log(logLevel, logMessage.Exception,
            "[CommandService] {Message}", logMessage.Message);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _client.MessageReceived -= HandleMessageAsync;
            _commands.CommandExecuted -= OnCommandExecutedAsync;
            _commands.Log -= OnCommandServiceLogAsync;

            _commandSemaphore?.Dispose();

            _disposed = true;
        }
    }
}