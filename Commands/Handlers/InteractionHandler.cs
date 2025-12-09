using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.VisualBasic;
using System.Reflection;

namespace Space_Cat_v3.Commands.Handlers;
public class InteractionHandler
{
    //регистрация DI
    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactionService;
    private readonly IServiceProvider _services;

    //конструктор для определения
    public InteractionHandler(DiscordSocketClient client, IServiceProvider services, InteractionService interactionService)
    {
        _client = client;
        _services = services;
        _interactionService = interactionService;
    }

    public async Task InitializeAsync()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        try
        {
            Console.WriteLine("🚀 Инициализация обработчика взаимодействий...");

            // Добавляем все публичные модули, наследующие InteractionModuleBase<T>
            await _interactionService.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
            Console.WriteLine($"✅ Зарегистрировано модулей: {_interactionService.Modules.Count}");

            // Подписываемся на события
            _client.InteractionCreated += HandleInteractionAsync;
            _interactionService.SlashCommandExecuted += SlashCommandExecutedAsync;
            _interactionService.ContextCommandExecuted += ContextCommandExecutedAsync;
            _interactionService.ComponentCommandExecuted += ComponentCommandExecutedAsync;

            Console.WriteLine("✅ Обработчик взаимодействий инициализирован");
            
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"❌ Ошибка инициализации обработчика: {ex.Message}");
            throw;
        }
        Console.ForegroundColor = ConsoleColor.White;
    }

    // Обработчик компонентов (кнопок, выпадающих списков)
    private Task ComponentCommandExecutedAsync(ComponentCommandInfo commandInfo, IInteractionContext context, IResult result)
    {
        
        if (!result.IsSuccess)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Ошибка компонента {commandInfo.Name}: {result.ErrorReason}");
        }
        return Task.CompletedTask;
    }

    // Обработчик контекстных команд (правая кнопка -> Apps)
    private Task ContextCommandExecutedAsync(ContextCommandInfo commandInfo, IInteractionContext context, IResult result)
    {
        if (!result.IsSuccess)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Ошибка контекстной команды {commandInfo.Name}: {result.ErrorReason}");
        }
        return Task.CompletedTask;
    }

    // Обработчик слеш-команд
    private async Task SlashCommandExecutedAsync(SlashCommandInfo commandInfo, IInteractionContext context, IResult result)
    {
        if (!result.IsSuccess)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Ошибка команды {commandInfo.Name}: {result.Error} - {result.ErrorReason}");

            string errorMessage = result.Error switch
            {
                InteractionCommandError.UnmetPrecondition => $"🚫 {result.ErrorReason}",
                InteractionCommandError.UnknownCommand => "❓ Неизвестная команда",
                InteractionCommandError.BadArgs => "📝 Неверные аргументы команды",
                InteractionCommandError.Exception => $"⚠️ Ошибка выполнения: {Truncate(result.ErrorReason, 100)}",
                InteractionCommandError.Unsuccessful => "⛔ Не удалось выполнить команду",
                _ => $"❌ Ошибка: {Truncate(result.ErrorReason, 100)}"
            };

            try
            {
                if (context.Interaction.HasResponded)
                {
                    await context.Interaction.FollowupAsync(errorMessage, ephemeral: true);
                }
                else
                {
                    await context.Interaction.RespondAsync(errorMessage, ephemeral: true);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Не удалось отправить сообщение об ошибке: {ex.Message}");
            }
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"✅ Команда выполнена: {commandInfo.Name} пользователем {context.User.Username}");
            
        }
        Console.ForegroundColor = ConsoleColor.White;
    }

    // Основной обработчик всех взаимодействий
    private async Task HandleInteractionAsync(SocketInteraction interaction)
    {
        try
        {
            // Создаём контекст выполнения
            var context = new SocketInteractionContext(_client, interaction);

            // Логируем тип взаимодействия
            LogInteractionType(interaction);

            // Выполняем команду           
            await _interactionService.ExecuteCommandAsync(context, _services);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Ошибка обработки взаимодействия: {ex.Message}");

            // Отправляем сообщение об ошибке пользователю
            try
            {
                if (interaction.HasResponded)
                {
                    await interaction.FollowupAsync(
                        "❌ Произошла ошибка при обработке команды. Попробуйте позже.",
                        ephemeral: true);
                }
                else
                {
                    await interaction.RespondAsync(
                        "❌ Произошла ошибка при обработке команды. Попробуйте позже.",
                        ephemeral: true);
                }
            }
            catch
            {
                // Игнорируем ошибки при отправке сообщения об ошибке
            }
        }
    }

    // Вспомогательный метод для логирования типа взаимодействия
    private void LogInteractionType(SocketInteraction interaction)
    {
        string interactionType = interaction.Type switch
        {
            InteractionType.ApplicationCommand => "Слеш-команда",
            InteractionType.MessageComponent => "Компонент сообщения",
            InteractionType.ApplicationCommandAutocomplete => "Автодополнение",
            InteractionType.ModalSubmit => "Модальное окно",
            _ => "Неизвестный тип"
        };
        
        Console.WriteLine($"📥 Взаимодействие: {interactionType} от {interaction.User.Username}");
    }

    // Вспомогательный метод для обрезки текста
    private string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        return text.Substring(0, maxLength - 3) + "...";
    }

    // Метод для очистки ресурсов (отписка от событий)
    public async Task DisposeAsync()
    {
        _client.InteractionCreated -= HandleInteractionAsync;
        _interactionService.SlashCommandExecuted -= SlashCommandExecutedAsync;
        _interactionService.ContextCommandExecuted -= ContextCommandExecutedAsync;
        _interactionService.ComponentCommandExecuted -= ComponentCommandExecutedAsync;

        await Task.CompletedTask;
        Console.WriteLine("✅ Обработчик взаимодействий очищен");
    }
}