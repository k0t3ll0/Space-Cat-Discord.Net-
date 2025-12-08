using Discord;
using Discord.Commands;
using Discord.WebSocket;

using Microsoft.Extensions.Logging;


using System.Text;
using System.Text.RegularExpressions;


namespace ReactionRoleModule
{
    [Group("reactionrole")]
    [Alias("rr")]
    [Summary("Управление ролями по реакциям")]
    [RequireUserPermission(GuildPermission.ManageRoles)]
    [RequireBotPermission(GuildPermission.ManageRoles)]
    public class ReactionRoleModule : ModuleBase<SocketCommandContext>
    {
        private readonly ReactionRoleService _reactionRoleService;
        private readonly ILogger<ReactionRoleModule> _logger;

        public ReactionRoleModule(ReactionRoleService reactionRoleService, ILogger<ReactionRoleModule> logger = null)
        {
            _reactionRoleService = reactionRoleService;
            _logger = logger;
        }

        #region Основные команды

        [Command("help")]
        [Alias("справка", "помощь")]
        [Summary("Показать справку по командам")]
        public async Task ShowHelpAsync()
        {
            var embed = new EmbedBuilder()
                .WithColor(Color.Blue)
                .WithTitle("📚 Команды ReactionRole")
                .WithDescription("Система выдачи ролей по реакциям")
                .AddField("📌 Основные команды",
                    $"`{Context.Client.CurrentUser.Mention} rr add <id_сообщения> <эмодзи> <@роль>` - Добавить привязку\n" +
                    $"`{Context.Client.CurrentUser.Mention} rr remove <id_сообщения> <эмодзи>` - Удалить привязку\n" +
                    $"`{Context.Client.CurrentUser.Mention} rr list [страница]` - Показать привязки\n" +
                    $"`{Context.Client.CurrentUser.Mention} rr createpanel [название] [описание]` - Создать панель\n" +
                    $"`{Context.Client.CurrentUser.Mention} rr info` - Статистика\n" +
                    $"`{Context.Client.CurrentUser.Mention} rr cleanup` - Очистка нерабочих привязок")
                .AddField("⚙️ Дополнительные параметры",
                    "При добавлении привязки можно указать:\n" +
                    "`[unique:true/false]` - уникальный выбор (только одна роль из группы)\n" +
                    "`[remove:true/false]` - забирать роль при снятии реакции\n" +
                    "`[group:название]` - группа для уникального выбора\n\n" +
                    "**Пример:**\n" +
                    $"`{Context.Client.CurrentUser.Mention} rr add 123456789 :fire: @Огненный unique:true group:элементы`")
                .AddField("🔧 Утилиты",
                    "`rr enable/disable` - Включить/выключить систему на сервере\n" +
                    "`rr export` - Экспорт привязок в файл\n" +
                    "`rr import` - Импорт привязок из файла\n" +
                    "`rr reset` - Полный сброс всех привязок")
                .AddField("💡 Как получить ID?",
                    "1. Включите **Режим разработчика** в настройках Discord\n" +
                    "2. ПКМ по сообщению → **Копировать ID**\n" +
                    "3. ПКМ по роли в списке участников → **Копировать ID**")
                .WithFooter($"Сервер: {Context.Guild.Name} | Всего команд: 12")
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("add")]
        [Summary("Добавить привязку роли к реакции")]
        public async Task AddReactionRoleAsync(
            [Summary("ID сообщения")] ulong messageId,
            [Summary("Эмодзи")] string emote,
            [Summary("Роль")] IRole role,
            [Summary("Уникальный выбор (true/false)")] bool isUnique = false,
            [Summary("Забирать роль при снятии (true/false)")] bool removeOnUnreact = true,
            [Summary("Группа для уникального выбора")] string group = null)
        {
            try
            {
                // Получаем сообщение
                IMessage message;
                try
                {
                    message = await Context.Channel.GetMessageAsync(messageId);
                    if (message == null)
                    {
                        // Пробуем найти в других каналах
                        message = await FindMessageInGuildAsync(messageId);
                        if (message == null)
                        {
                            await ReplyAsync("❌ Сообщение не найдено! Проверьте ID и убедитесь, что бот имеет доступ к каналу.");
                            return;
                        }
                    }
                }
                catch
                {
                    await ReplyAsync("❌ Не удалось получить сообщение. Проверьте ID и права бота.");
                    return;
                }

                // Проверяем права бота на управление ролью
                var botUser = Context.Guild.CurrentUser;
                var botHighestRole = botUser.Roles.OrderByDescending(r => r.Position).FirstOrDefault();

                if (role.Position >= botHighestRole.Position)
                {
                    await ReplyAsync("❌ Роль находится выше или на одном уровне с высшей ролью бота!");
                    return;
                }

                // Проверяем, может ли бот добавлять реакции
                var channel = message.Channel as SocketGuildChannel;
                var botPermissions = channel.GetPermissionOverwrite(botUser).Value;
                if (botPermissions.AddReactions is PermValue.Deny  || botPermissions.ReadMessageHistory is PermValue.Deny)
                {
                    await ReplyAsync("❌ Боту необходимы права: **Добавлять реакции** и **Читать историю сообщений**!");
                    return;
                }

                // Парсим эмодзи
                var parsedEmote = ParseEmote(emote);
                if (parsedEmote == null)
                {
                    await ReplyAsync("❌ Неверный формат эмодзи! Используйте:\n" +
                                    "• Стандартные: `🔥`, `😀`\n" +
                                    "• Кастомные: `<:name:123456789>`\n" +
                                    "• Анимированные: `<a:name:123456789>`");
                    return;
                }

                // Создаем привязку
                var binding = new ReactionRoleService.ReactionRoleBinding
                {
                    MessageId = messageId,
                    ChannelId = message.Channel.Id,
                    GuildId = Context.Guild.Id,
                    Emote = emote,
                    RoleId = role.Id,
                    RemoveOnUnreact = removeOnUnreact,
                    IsUnique = isUnique,
                    UniqueGroup = group,
                    CreatedBy = Context.User.Id
                };

                // Добавляем привязку
                await _reactionRoleService.AddBindingAsync(binding);

                // Добавляем реакцию к сообщению
                try
                {
                    await message.AddReactionAsync(parsedEmote);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Не удалось добавить реакцию к сообщению");
                    await ReplyAsync("⚠️ Привязка добавлена, но не удалось добавить реакцию к сообщению. Проверьте права бота.");
                }

                // Создаем embed ответа
                var embed = new EmbedBuilder()
                    .WithColor(Color.Green)
                    .WithTitle("✅ Привязка добавлена")
                    .WithDescription($"[Перейти к сообщению]({message.GetJumpUrl()})")
                    .AddField("Сообщение", $"`{messageId}`", true)
                    .AddField("Канал", $"<#{message.Channel.Id}>", true)
                    .AddField("Эмодзи", emote, true)
                    .AddField("Роль", role.Mention, true)
                    .AddField("Уникальный", isUnique ? "✅ Да" : "❌ Нет", true)
                    .AddField("Группа", string.IsNullOrEmpty(group) ? "—" : group, true)
                    .AddField("Снятие роли", removeOnUnreact ? "✅ Авто" : "❌ Ручное", true)
                    .WithFooter($"ID: {messageId} | Добавил: {Context.User.Username}")
                    .WithTimestamp(DateTimeOffset.Now)
                    .Build();

                await ReplyAsync(embed: embed);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка при добавлении привязки");

                var errorEmbed = new EmbedBuilder()
                    .WithColor(Color.Red)
                    .WithTitle("❌ Ошибка")
                    .WithDescription("Не удалось добавить привязку.")
                    .AddField("Возможные причины",
                        "• Неверный ID сообщения\n" +
                        "• Нет прав у бота\n" +
                        "• Роль выше роли бота\n" +
                        "• Эмодзи недоступен боту")
                    .Build();

                await ReplyAsync(embed: errorEmbed);
            }
        }

        [Command("remove")]
        [Summary("Удалить привязку роли к реакции")]
        public async Task RemoveReactionRoleAsync(
            [Summary("ID сообщения")] ulong messageId,
            [Summary("Эмодзи")] string emote)
        {
            try
            {
                var success = await _reactionRoleService.RemoveBindingAsync(Context.Guild.Id, messageId, emote);

                if (success)
                {
                    var embed = new EmbedBuilder()
                        .WithColor(Color.Green)
                        .WithTitle("✅ Привязка удалена")
                        .AddField("Сообщение", $"`{messageId}`", true)
                        .AddField("Эмодзи", emote, true)
                        .WithFooter($"Удалил: {Context.User.Username}")
                        .Build();

                    await ReplyAsync(embed: embed);
                }
                else
                {
                    await ReplyAsync("❌ Привязка не найдена!");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка при удалении привязки");
                await ReplyAsync("❌ Произошла ошибка при удалении привязки!");
            }
        }

        [Command("list")]
        [Alias("show", "ls")]
        [Summary("Показать все привязки на сервере")]
        public async Task ListReactionRolesAsync([Summary("Номер страницы")] int page = 1)
        {
            try
            {
                var bindings = _reactionRoleService.GetGuildBindings(Context.Guild.Id);

                if (bindings.Count == 0)
                {
                    var embed = new EmbedBuilder()
                        .WithColor(Color.Blue)
                        .WithTitle("📋 Привязки ролей")
                        .WithDescription("На этом сервере нет привязок ролей к реакциям.")
                        .AddField("Как добавить?",
                            $"Используйте команду:\n`{Context.Client.CurrentUser.Mention} rr add <id_сообщения> <эмодзи> @роль`")
                        .Build();

                    await ReplyAsync(embed: embed);
                    return;
                }

                // Пагинация
                const int itemsPerPage = 10;
                var totalPages = (int)Math.Ceiling(bindings.Count / (double)itemsPerPage);
                page = Math.Clamp(page, 1, totalPages);

                var pageBindings = bindings
                    .Skip((page - 1) * itemsPerPage)
                    .Take(itemsPerPage)
                    .ToList();

                var embedBuilder = new EmbedBuilder()
                    .WithColor(Color.Blue)
                    .WithTitle($"📋 Привязки ролей ({bindings.Count} всего)")
                    .WithFooter($"Страница {page}/{totalPages} • Сервер: {Context.Guild.Name}")
                    .WithTimestamp(DateTimeOffset.Now);

                foreach (var binding in pageBindings)
                {
                    var role = Context.Guild.GetRole(binding.RoleId);
                    var roleName = role?.Mention ?? "`Роль удалена`";

                    var channel = Context.Guild.GetTextChannel(binding.ChannelId);
                    var channelName = channel?.Mention ?? "`Канал удален`";

                    var fieldValue = new StringBuilder();
                    fieldValue.AppendLine($"**Роль:** {roleName}");
                    fieldValue.AppendLine($"**Канал:** {channelName}");
                    fieldValue.AppendLine($"**Сообщение:** `{binding.MessageId}`");
                    fieldValue.AppendLine($"**Режим:** {(binding.IsUnique ? "Уникальный" : "Множественный")}");
                    if (!string.IsNullOrEmpty(binding.UniqueGroup))
                        fieldValue.AppendLine($"**Группа:** `{binding.UniqueGroup}`");
                    fieldValue.AppendLine($"**Снятие:** {(binding.RemoveOnUnreact ? "Авто" : "Ручное")}");
                    fieldValue.AppendLine($"**Добавлена:** <t:{((DateTimeOffset)binding.CreatedAt).ToUnixTimeSeconds()}:R>");

                    embedBuilder.AddField($"{binding.Emote}", fieldValue.ToString());
                }

                await ReplyAsync(embed: embedBuilder.Build());
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка при получении списка привязок");
                await ReplyAsync("❌ Произошла ошибка при получении списка привязок!");
            }
        }

        [Command("createpanel")]
        [Alias("panel", "create")]
        [Summary("Создать панель выбора ролей")]
        public async Task CreateRolePanelAsync(
            [Summary("Название панели")] string title = "🎮 Выбор ролей",
            [Remainder][Summary("Описание")] string description = "Нажмите на реакции ниже, чтобы получить соответствующие роли")
        {
            try
            {
                var embed = new EmbedBuilder()
                    .WithColor(Color.Gold)
                    .WithTitle(title)
                    .WithDescription(description + $"\n\n📋 **Доступные роли будут появляться здесь**")
                    .AddField("ℹ️ Как использовать?",
                        "1. Добавьте реакцию к этому сообщению\n" +
                        "2. Используйте команду добавления привязки\n" +
                        $"3. Пример: `{Context.Client.CurrentUser.Mention} rr add {{id}} :emoji: @роль`")
                    .WithFooter($"ID сообщения появится после отправки • Создал: {Context.User.Username}")
                    .WithTimestamp(DateTimeOffset.Now)
                    .Build();

                var message = await ReplyAsync(embed: embed);

                var responseEmbed = new EmbedBuilder()
                    .WithColor(Color.Green)
                    .WithTitle("✅ Панель создана")
                    .WithDescription($"Панель ролей успешно создана!")
                    .AddField("Ссылка на сообщение", $"[Перейти]({message.GetJumpUrl()})")
                    .AddField("ID сообщения", $"`{message.Id}`")
                    .AddField("Пример команды для добавления",
                        $"`{Context.Client.CurrentUser.Mention} rr add {message.Id} :fire: @Огненный`")
                    .WithFooter($"Теперь добавьте привязки с помощью команды rr add")
                    .Build();

                await ReplyAsync(embed: responseEmbed);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка при создании панели");
                await ReplyAsync("❌ Произошла ошибка при создании панели!");
            }
        }

        #endregion

        #region Команды управления

        [Command("enable")]
        [Summary("Включить систему ролей по реакциям на сервере")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task EnableSystemAsync()
        {
            try
            {
                await _reactionRoleService.SetGuildEnabledAsync(Context.Guild.Id, true);

                var embed = new EmbedBuilder()
                    .WithColor(Color.Green)
                    .WithTitle("✅ Система включена")
                    .WithDescription("Система выдачи ролей по реакциям теперь **активна** на этом сервере.")
                    .AddField("Статус", "🟢 Активна")
                    .WithFooter($"Включил: {Context.User.Username}")
                    .Build();

                await ReplyAsync(embed: embed);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка при включении системы");
                await ReplyAsync("❌ Ошибка при включении системы!");
            }
        }

        [Command("disable")]
        [Summary("Отключить систему ролей по реакциям на сервере")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task DisableSystemAsync()
        {
            try
            {
                await _reactionRoleService.SetGuildEnabledAsync(Context.Guild.Id, false);

                var embed = new EmbedBuilder()
                    .WithColor(Color.Orange)
                    .WithTitle("⏸️ Система отключена")
                    .WithDescription("Система выдачи ролей по реакциям теперь **неактивна** на этом сервере.\nСуществующие реакции продолжат работать.")
                    .AddField("Статус", "🟡 Приостановлена")
                    .WithFooter($"Отключил: {Context.User.Username}")
                    .Build();

                await ReplyAsync(embed: embed);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка при отключении системы");
                await ReplyAsync("❌ Ошибка при отключении системы!");
            }
        }

        [Command("cleanup")]
        [Alias("clean", "fix")]
        [Summary("Очистить неработающие привязки")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task CleanupBindingsAsync()
        {
            try
            {
                var bindings = _reactionRoleService.GetGuildBindings(Context.Guild.Id);
                var brokenBindings = new List<ReactionRoleService.ReactionRoleBinding>();
                var validBindings = new List<ReactionRoleService.ReactionRoleBinding>();

                foreach (var binding in bindings)
                {
                    // Проверяем существование роли
                    var role = Context.Guild.GetRole(binding.RoleId);
                    if (role == null)
                    {
                        brokenBindings.Add(binding);
                        continue;
                    }

                    // Проверяем существование канала
                    var channel = Context.Guild.GetTextChannel(binding.ChannelId);
                    if (channel == null)
                    {
                        brokenBindings.Add(binding);
                        continue;
                    }

                    // Проверяем существование сообщения
                    try
                    {
                        var message = await channel.GetMessageAsync(binding.MessageId);
                        if (message == null)
                        {
                            brokenBindings.Add(binding);
                            continue;
                        }
                    }
                    catch
                    {
                        brokenBindings.Add(binding);
                        continue;
                    }

                    validBindings.Add(binding);
                }

                // Удаляем нерабочие привязки
                foreach (var broken in brokenBindings)
                {
                    await _reactionRoleService.RemoveBindingAsync(Context.Guild.Id, broken.MessageId, broken.Emote);
                }

                var embed = new EmbedBuilder()
                    .WithColor(brokenBindings.Count > 0 ? Color.Orange : Color.Green)
                    .WithTitle("🧹 Очистка привязок")
                    .WithDescription($"Проверено привязок: **{bindings.Count}**")
                    .AddField("✅ Рабочие", validBindings.Count.ToString(), true)
                    .AddField("❌ Удалено", brokenBindings.Count.ToString(), true)
                    .AddField("📊 Результат", $"{validBindings.Count}/{bindings.Count} привязок в порядке")
                    .WithFooter($"Очистка выполнена • {Context.User.Username}")
                    .Build();

                await ReplyAsync(embed: embed);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка при очистке привязок");
                await ReplyAsync("❌ Ошибка при очистке привязок!");
            }
        }

        [Command("info")]
        [Alias("stats", "статистика")]
        [Summary("Показать статистику системы")]
        public async Task ShowStatsAsync()
        {
            try
            {
                var bindings = _reactionRoleService.GetGuildBindings(Context.Guild.Id);
                var isEnabled = _reactionRoleService.IsGuildEnabled(Context.Guild.Id);

                var uniqueGroups = bindings
                    .Where(b => !string.IsNullOrEmpty(b.UniqueGroup))
                    .GroupBy(b => b.UniqueGroup)
                    .Count();

                var embed = new EmbedBuilder()
                    .WithColor(isEnabled ? Color.Green : Color.Orange)
                    .WithTitle("📊 Статистика ReactionRole")
                    .AddField("Статус системы", isEnabled ? "🟢 Активна" : "🟡 Приостановлена", true)
                    .AddField("Всего привязок", bindings.Count.ToString(), true)
                    .AddField("Уникальные группы", uniqueGroups.ToString(), true)
                    .AddField("Сообщения с привязками",
                        bindings.Select(b => b.MessageId).Distinct().Count().ToString(), true)
                    .AddField("Авто-снятие ролей",
                        bindings.Count(b => b.RemoveOnUnreact).ToString(), true)
                    .AddField("Уникальный выбор",
                        bindings.Count(b => b.IsUnique).ToString(), true)
                    .AddField("Последние привязки",
                        bindings.OrderByDescending(b => b.CreatedAt)
                            .Take(3)
                            .Select(b =>
                                $"{b.Emote} → <@&{b.RoleId}> (<t:{((DateTimeOffset)b.CreatedAt).ToUnixTimeSeconds()}:R>)")
                            .DefaultIfEmpty("Нет привязок")
                            .Aggregate((a, b) => $"{a}\n{b}"))
                    .WithFooter($"Сервер: {Context.Guild.Name} • {DateTime.Now:yyyy-MM-dd}")
                    .Build();

                await ReplyAsync(embed: embed);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка при получении статистики");
                await ReplyAsync("❌ Ошибка при получении статистики!");
            }
        }

        [Command("reset")]
        [Summary("Полный сброс всех привязок на сервере")]
        [RequireUserPermission(GuildPermission.Administrator)]
        public async Task ResetAllAsync()
        {
            // Запрос подтверждения
            var confirmEmbed = new EmbedBuilder()
                .WithColor(Color.Red)
                .WithTitle("⚠️ ОПАСНОЕ ДЕЙСТВИЕ")
                .WithDescription("Вы собираетесь **полностью удалить все привязки** на этом сервере!")
                .AddField("Последствия",
                    "• Все привязки будут удалены безвозвратно\n" +
                    "• Реакции останутся на сообщениях\n" +
                    "• Роли у пользователей останутся\n" +
                    "• **Действие необратимо!**")
                .AddField("Подтверждение",
                    $"Напишите `confirm` в течение 30 секунд для подтверждения.\n" +
                    $"Или напишите `cancel` для отмены.")
                .WithFooter($"Запрошено: {Context.User.Username}")
                .Build();

            await ReplyAsync(embed: confirmEmbed);

            // Ожидание подтверждения
            var response = await NextMessageAsync(TimeSpan.FromSeconds(30), Context.User.Id, Context.Channel.Id);

            if (response?.Content?.ToLower() == "confirm")
            {
                var count = await _reactionRoleService.RemoveAllBindingsForGuildAsync(Context.Guild.Id);

                var resultEmbed = new EmbedBuilder()
                    .WithColor(Color.Green)
                    .WithTitle("✅ Сброс выполнен")
                    .WithDescription($"Удалено **{count}** привязок.")
                    .AddField("Статус", "Все привязки удалены")
                    .WithFooter($"Сброс выполнен • {Context.User.Username}")
                    .Build();

                await ReplyAsync(embed: resultEmbed);
            }
            else
            {
                var cancelEmbed = new EmbedBuilder()
                    .WithColor(Color.Blue)
                    .WithTitle("⏹️ Действие отменено")
                    .WithDescription("Сброс привязок отменен пользователем.")
                    .Build();

                await ReplyAsync(embed: cancelEmbed);
            }
        }

        #endregion

        #region Вспомогательные методы

        private async Task<IMessage> FindMessageInGuildAsync(ulong messageId)
        {
            foreach (var channel in Context.Guild.TextChannels)
            {
                try
                {
                    if (!channel.GetPermissionOverwrite(Context.Guild.CurrentUser).HasValue ||
                        channel.GetPermissionOverwrite(Context.Guild.CurrentUser).Value.ViewChannel is PermValue.Deny)
                        continue;

                    var message = await channel.GetMessageAsync(messageId);
                    if (message != null)
                        return message;
                }
                catch
                {
                    continue;
                }
            }
            return null;
        }

        private IEmote ParseEmote(string emoteString)
        {
            emoteString = emoteString.Trim();

            // Пробуем распарсить как кастомный эмодзи
            if (Emote.TryParse(emoteString, out var emote))
                return emote;

            // Пробуем как эмодзи Unicode
            if (!string.IsNullOrWhiteSpace(emoteString) && emoteString.Length <= 10)
                return new Emoji(emoteString);

            return null;
        }

        private async Task<SocketMessage> NextMessageAsync(TimeSpan timeout, ulong userId, ulong channelId)
        {
            var tcs = new TaskCompletionSource<SocketMessage>();
            var cancellationToken = new System.Threading.CancellationTokenSource(timeout);

            cancellationToken.Token.Register(() => tcs.TrySetResult(null));

            Context.Client.MessageReceived += OnMessageReceived;

            try
            {
                var result = await tcs.Task;
                return result;
            }
            finally
            {
                Context.Client.MessageReceived -= OnMessageReceived;
                cancellationToken.Dispose();
            }

            Task OnMessageReceived(SocketMessage message)
            {
                if (message.Channel.Id == channelId && message.Author.Id == userId)
                {
                    tcs.TrySetResult(message);
                }
                return Task.CompletedTask;
            }
        }

        #endregion
    }
}