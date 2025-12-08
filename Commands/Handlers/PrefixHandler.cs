using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Space_Cat_v3.Commands.Handlers;
public class PrefixHandler : IDisposable
{
    private readonly DiscordSocketClient _client;
    private readonly CommandService _commands;
    private readonly IServiceProvider _services;
    private readonly ReactionRoleService _reactionsRoles;
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
        _commandSemaphore = new SemaphoreSlim(1, 1);
        _reactionsRoles = services.GetRequiredService<ReactionRoleService>();
        // Получаем префиксы из конфигурации
        var prefixConfig = config["prefix"] ?? "!";
        _prefixes = ParsePrefixes(prefixConfig);

        if (_prefixes.Count == 0)
        {
            _prefixes.Add('!'); // Значение по умолчанию
        }

        _logger?.LogInformation("Инициализирован PrefixHandler с префиксами: {Prefixes}",
            string.Join(", ", _prefixes.Select(p => $"'{p}'")));
    }

    public async Task InitializeAsync()
    {
        try
        {
            _client.MessageReceived += HandleMessageAsync;
            // Подписываемся на события CommandService для обработки результатов
            _commands.CommandExecuted += OnCommandExecutedAsync;
            _commands.Log += OnCommandServiceLogAsync;

            _logger?.LogInformation("PrefixHandler инициализирован успешно");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ошибка при инициализации PrefixHandler");
            throw;
        }
    }

    // Добавление модуля асинхронно
    public async Task AddModuleAsync<T>() where T : IModuleBase
    {
        try
        {
            await _commands.AddModuleAsync<T>(_services);
            _logger?.LogInformation("Модуль {ModuleName} успешно добавлен", typeof(T).Name);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ошибка при добавлении модуля {ModuleName}", typeof(T).Name);
            throw;
        }
    }

    // Добавление модуля по типу
    public async Task AddModuleAsync(Type moduleType)
    {
        if (!typeof(IModuleBase).IsAssignableFrom(moduleType))
        {
            throw new ArgumentException($"Тип {moduleType.Name} не реализует IModuleBase");
        }

        try
        {
            await _commands.AddModuleAsync(moduleType, _services);
            _logger?.LogInformation("Модуль {ModuleName} успешно добавлен", moduleType.Name);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ошибка при добавлении модуля {ModuleName}", moduleType.Name);
            throw;
        }
    }

    // Добавление нескольких модулей
    public async Task AddModulesAsync(params Type[] moduleTypes)
    {
        foreach (var moduleType in moduleTypes)
        {
            await AddModuleAsync(moduleType);
        }
    }

    // Основной обработчик сообщений
    private async Task HandleMessageAsync(SocketMessage messageParam)
    {
        // Быстрая проверка, чтобы не обрабатывать ненужные сообщения
        if (!ShouldProcessMessage(messageParam))
            return;

        var message = (SocketUserMessage)messageParam;
        var context = new SocketCommandContext(_client, message);

        try
        {
            // Используем семафор для предотвращения race conditions
            await _commandSemaphore.WaitAsync();

            // Пытаемся выполнить команду для каждого префикса
            foreach (var prefix in _prefixes)
            {
                int argPos = 0;

                if (message.HasCharPrefix(prefix, ref argPos) ||
                    message.HasMentionPrefix(_client.CurrentUser, ref argPos))
                {
                    await ExecuteCommandAsync(context, argPos);
                    return; // Команда найдена и выполнена
                }
            }

            // Проверяем строковые префиксы (для многосимвольных префиксов)
            if (TryGetStringPrefix(message, out int argPosString))
            {
                await ExecuteCommandAsync(context, argPosString);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ошибка при обработке сообщения от {User}", message.Author);

            try
            {
                // Отправляем сообщение об ошибке пользователю
                if (message.Channel is ITextChannel textChannel)
                {
                    var embed = new EmbedBuilder()
                        .WithColor(Color.Red)
                        .WithDescription("❌ Произошла ошибка при выполнении команды")
                        .WithFooter("Попробуйте позже или обратитесь к администратору")
                        .Build();

                    await message.Channel.SendMessageAsync(embed: embed);
                }
            }
            catch (Exception sendEx)
            {
                _logger?.LogWarning(sendEx, "Не удалось отправить сообщение об ошибке");
            }
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
            context.User.Username,
            context.Message.Content);

        var result = await _commands.ExecuteAsync(context, argPos, _services);

        // Логируем результат выполнения
        if (!result.IsSuccess)
        {
            _logger?.LogWarning("Команда не выполнена: {Error} - {Reason}",
                result.Error,
                result.ErrorReason);
        }
        else
        {
            _logger?.LogDebug("Команда успешно выполнена: {Command}",
                context.Message.Content);
        }
    }

    // Обработчик результатов выполнения команд
    private async Task OnCommandExecutedAsync(Optional<CommandInfo> command, ICommandContext context, IResult result)
    {
        if (!result.IsSuccess)
        {
            string errorMessage = result.Error switch
            {
                CommandError.UnknownCommand => "Неизвестная команда",
                CommandError.ParseFailed => "Не удалось разобрать аргументы команды",
                CommandError.BadArgCount => "Неверное количество аргументов",
                CommandError.ObjectNotFound => "Объект не найден",
                CommandError.MultipleMatches => "Найдено несколько совпадений",
                CommandError.UnmetPrecondition => GetFriendlyPreconditionError(result.ErrorReason),
                CommandError.Exception => "Внутренняя ошибка при выполнении команды",
                CommandError.Unsuccessful => "Команда не выполнена",
                _ => "Неизвестная ошибка"
            };

            _logger?.LogWarning("Ошибка команды {Command}: {Error}",
                command.IsSpecified ? command.Value.Name : "unknown",
                errorMessage);

            // Отправляем сообщение об ошибке пользователю
            try
            {
                if (context.Channel is ITextChannel textChannel)
                {
                    var embed = new EmbedBuilder()
                        .WithColor(Color.Orange)
                        .WithDescription($"⚠️ {errorMessage}")
                        .Build();

                    await context.Channel.SendMessageAsync(embed: embed);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Не удалось отправить сообщение об ошибке");
            }
        }
    }

    // Обработчик логов CommandService
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

    // Проверка, нужно ли обрабатывать сообщение
    private bool ShouldProcessMessage(SocketMessage message)
    {
        // Проверяем тип сообщения
        if (message is not SocketUserMessage userMessage)
            return false;

        // Игнорируем сообщения от ботов
        if (userMessage.Author.IsBot)
            return false;

        // Игнорируем системные сообщения
        if (userMessage.Author.IsWebhook)
            return false;

        // Игнорируем пустые сообщения
        if (string.IsNullOrWhiteSpace(userMessage.Content))
            return false;

        return true;
    }

    // Парсинг префиксов из конфигурации
    private List<char> ParsePrefixes(string prefixConfig)
    {
        var prefixes = new List<char>();

        if (!string.IsNullOrWhiteSpace(prefixConfig))
        {
            // Поддерживаем несколько префиксов через запятую
            var prefixStrings = prefixConfig.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s));

            foreach (var prefix in prefixStrings)
            {
                // Берем только первый символ, так как HasCharPrefix работает с одиночными символами
                if (prefix.Length > 0)
                {
                    prefixes.Add(prefix[0]);
                }
            }
        }

        return prefixes;
    }

    // Проверка строковых префиксов (для многосимвольных)
    private bool TryGetStringPrefix(SocketUserMessage message, out int argPos)
    {
        argPos = 0;

        // Здесь можно реализовать проверку многосимвольных префиксов
        // Например, если у вас есть префикс "!!"
        var content = message.Content;

        // Пример: проверка префикса "!!"
        if (content.StartsWith("&&"))
        {
            argPos = 2;
            return true;
        }

        return false;
    }

    // Получение текущих префиксов
    public IReadOnlyList<char> GetPrefixes() => _prefixes.AsReadOnly();

    // Добавление временного префикса
    public void AddTemporaryPrefix(char prefix)
    {
        if (!_prefixes.Contains(prefix))
        {
            _prefixes.Add(prefix);
            _logger?.LogInformation("Добавлен временный префикс: '{Prefix}'", prefix);
        }
    }

    // Удаление временного префикса
    public bool RemoveTemporaryPrefix(char prefix)
    {
        // Не удаляем префиксы из конфигурации, только добавленные временно
        // (в этой простой реализации удаляем любой префикс, кроме первого)
        if (_prefixes.Count > 1 && _prefixes.Contains(prefix))
        {
            _prefixes.Remove(prefix);
            _logger?.LogInformation("Удален временный префикс: '{Prefix}'", prefix);
            return true;
        }

        return false;
    }

    // Очистка ресурсов
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // Отписываемся от событий
                if (_client != null)
                    _client.MessageReceived -= HandleMessageAsync;

                if (_commands != null)
                {
                    _commands.CommandExecuted -= OnCommandExecutedAsync;
                    _commands.Log -= OnCommandServiceLogAsync;
                }

                _commandSemaphore?.Dispose();
                _logger?.LogInformation("PrefixHandler очищен");
            }

            _disposed = true;
        }
    }

    private string GetFriendlyPreconditionError(string errorReason)
    {
        if (errorReason.Contains("Administrator") || errorReason.Contains("Admin"))
            return "Эта команда доступна только администраторам";

        if (errorReason.Contains("Moderator") || errorReason.Contains("Mod"))
            return "Эта команда доступна только модераторам";

        if (errorReason.Contains("Permission"))
            return "У вас недостаточно прав для выполнения этой команды";

        return errorReason ?? "Не выполнены предварительные условия";
    }

    ~PrefixHandler()
    {
        Dispose(false);
    }
}