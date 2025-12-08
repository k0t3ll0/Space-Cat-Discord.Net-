using Discord;
using Discord.Commands;
using Microsoft.Extensions.Logging;

namespace Space_Cat_v3.Commands.Modules
{
    
    [Name("ReactionRole")]  // Добавляем имя модуля
    [Summary("Управление ролями по реакциям")]
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

        [Command("rr help")]
        [Alias("reactionrole help", "rr справка")]
        [Summary("Показать справку по командам ReactionRole")]
        public async Task ShowHelpAsync()
        {
            var embed = new EmbedBuilder()
                .WithColor(Color.Blue)
                .WithTitle("📚 Команды ReactionRole")
                .WithDescription("Система выдачи ролей по реакциям")
                .AddField("📌 Основные команды",
                    $"`!rr add <id_сообщения> <эмодзи> <@роль>` - Добавить привязку\n" +
                    $"`!rr remove <id_сообщения> <эмодзи>` - Удалить привязку\n" +
                    $"`!rr list` - Показать привязки\n" +
                    $"`!rr createpanel` - Создать панель\n" +
                    $"`!rr info` - Статистика\n" +
                    $"`!rr cleanup` - Очистка нерабочих привязок")
                .AddField("⚙️ Дополнительные параметры",
                    "При добавлении привязки можно указать:\n" +
                    "`[unique:true/false]` - уникальный выбор\n" +
                    "`[remove:true/false]` - забирать роль при снятии\n" +
                    "`[group:название]` - группа для уникального выбора")
                .AddField("🔧 Утилиты",
                    "`!rr enable/disable` - Включить/выключить систему\n" +
                    "`!rr reset` - Полный сброс всех привязок")
                .WithFooter($"Сервер: {Context.Guild.Name}")
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("rr add")]
        [Summary("Добавить привязку роли к реакции")]
        [RequireUserPermission(GuildPermission.ManageRoles)]
        [RequireBotPermission(GuildPermission.ManageRoles)]
        public async Task AddReactionRoleAsync(
            [Summary("ID сообщения")] ulong messageId,
            [Summary("Эмодзи")] string emote,
            [Summary("Роль")] IRole role,
            [Summary("Уникальный выбор")] bool isUnique = false,
            [Summary("Забирать роль при снятии")] bool removeOnUnreact = true,
            [Summary("Группа")] string group = null)
        {
            try
            {
                // Получаем сообщение
                IMessage message = null;
                try
                {
                    message = await Context.Channel.GetMessageAsync(messageId);
                }
                catch
                {
                    // Пробуем найти в других каналах
                    foreach (var channel in Context.Guild.TextChannels)
                    {
                        try
                        {
                            message = await channel.GetMessageAsync(messageId);
                            if (message != null) break;
                        }
                        catch { continue; }
                    }
                }

                if (message == null)
                {
                    await ReplyAsync("❌ Сообщение не найдено! Проверьте ID.");
                    return;
                }

                // Проверяем права бота
                var botUser = Context.Guild.CurrentUser;
                if (role.Position >= botUser.Hierarchy)
                {
                    await ReplyAsync("❌ Роль находится выше или на одном уровне с ролью бота!");
                    return;
                }

                // Парсим эмодзи
                IEmote parsedEmote = null;
                if (Emote.TryParse(emote, out var customEmote))
                {
                    parsedEmote = customEmote;
                }
                else if (!string.IsNullOrWhiteSpace(emote))
                {
                    parsedEmote = new Emoji(emote);
                }

                if (parsedEmote == null)
                {
                    await ReplyAsync("❌ Неверный формат эмодзи!");
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
                try
                {
                    await _reactionRoleService.AddBindingAsync(binding);
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("уже существует"))
                    {
                        await ReplyAsync("❌ Привязка уже существует!");
                        return;
                    }
                    throw;
                }

                // Добавляем реакцию
                try
                {
                    await message.AddReactionAsync(parsedEmote);
                }
                catch
                {
                    await ReplyAsync("⚠️ Привязка добавлена, но не удалось добавить реакцию.");
                }

                var embed = new EmbedBuilder()
                    .WithColor(Color.Green)
                    .WithTitle("✅ Привязка добавлена")
                    .AddField("Сообщение", $"`{messageId}`", true)
                    .AddField("Эмодзи", emote, true)
                    .AddField("Роль", role.Mention, true)
                    .AddField("Уникальный", isUnique ? "Да" : "Нет", true)
                    .WithFooter($"Добавил: {Context.User.Username}")
                    .Build();

                await ReplyAsync(embed: embed);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка при добавлении привязки");
                await ReplyAsync("❌ Произошла ошибка при добавлении привязки!");
            }
        }

        [Command("rr remove")]
        [Summary("Удалить привязку роли к реакции")]
        [RequireUserPermission(GuildPermission.ManageRoles)]
        public async Task RemoveReactionRoleAsync(
            [Summary("ID сообщения")] ulong messageId,
            [Summary("Эмодзи")] string emote)
        {
            try
            {
                var success = await _reactionRoleService.RemoveBindingAsync(Context.Guild.Id, messageId, emote);
                await ReplyAsync(success ? "✅ Привязка удалена!" : "❌ Привязка не найдена!");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка при удалении привязки");
                await ReplyAsync("❌ Произошла ошибка при удалении привязки!");
            }
        }

        [Command("rr list")]
        [Alias("rr show")]
        [Summary("Показать все привязки на сервере")]
        public async Task ListReactionRolesAsync()
        {
            try
            {
                var bindings = _reactionRoleService.GetGuildBindings(Context.Guild.Id);

                if (bindings.Count == 0)
                {
                    await ReplyAsync("ℹ️ На этом сервере нет привязок ролей к реакциям.");
                    return;
                }

                var embed = new EmbedBuilder()
                    .WithColor(Color.Blue)
                    .WithTitle($"📋 Привязки ролей ({bindings.Count})")
                    .WithFooter($"Сервер: {Context.Guild.Name}");
                    

                foreach (var binding in bindings.Take(10))
                {
                    var role = Context.Guild.GetRole(binding.RoleId);
                    var roleName = role?.Mention ?? "Роль удалена";

                    embed.AddField(
                        $"{binding.Emote} → {roleName}",
                        $"Сообщение: `{binding.MessageId}`\nУникальный: {(binding.IsUnique ? "Да" : "Нет")}");
                }

                if (bindings.Count > 10)
                {
                    embed.WithDescription($"Показано 10 из {bindings.Count} привязок");
                }

                await ReplyAsync(embed: embed.Build());
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка при получении списка привязок");
                await ReplyAsync("❌ Произошла ошибка при получении списка привязок!");
            }
        }

        [Command("rr createpanel")]
        [Summary("Создать панель выбора ролей")]
        [RequireUserPermission(GuildPermission.ManageRoles)]
        public async Task CreateRolePanelAsync()
        {
            try
            {
                var embed = new EmbedBuilder()
                    .WithColor(Color.Gold)
                    .WithTitle("🎮 Выбор ролей")
                    .WithDescription("Нажмите на реакции ниже, чтобы получить соответствующие роли")
                    .WithFooter($"Создал: {Context.User.Username}")
                    .Build();

                var message = await ReplyAsync(embed: embed);

                await ReplyAsync($"✅ Панель создана!\nID сообщения: `{message.Id}`\n" +
                                $"Добавьте привязки командой: `!rr add {message.Id} :emoji: @роль`");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка при создании панели");
                await ReplyAsync("❌ Произошла ошибка при создании панели!");
            }
        }

        [Command("rr info")]
        [Alias("rr stats")]
        [Summary("Показать статистику системы")]
        public async Task ShowStatsAsync()
        {
            try
            {
                var bindings = _reactionRoleService.GetGuildBindings(Context.Guild.Id);
                var isEnabled = _reactionRoleService.IsGuildEnabled(Context.Guild.Id);

                var embed = new EmbedBuilder()
                    .WithColor(isEnabled ? Color.Green : Color.Orange)
                    .WithTitle("📊 Статистика ReactionRole")
                    .AddField("Статус", isEnabled ? "🟢 Активна" : "🟡 Приостановлена", true)
                    .AddField("Привязок", bindings.Count.ToString(), true)
                    .AddField("Уникальные", bindings.Count(b => b.IsUnique).ToString(), true)
                    .WithFooter($"Сервер: {Context.Guild.Name}")
                    .Build();

                await ReplyAsync(embed: embed);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Ошибка при получении статистики");
                await ReplyAsync("❌ Ошибка при получении статистики!");
            }
        }

        #endregion
    }
}