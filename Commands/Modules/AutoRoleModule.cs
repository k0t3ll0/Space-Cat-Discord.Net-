using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Space_Cat_v3.Commands.Handlers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Space_Cat_v3.Commands.Modules
{

    public class SimpleAutoRoleModule : ModuleBase<SocketCommandContext>
    {
        private readonly SimpleAutoRoleService _autoRole;

        public SimpleAutoRoleModule(SimpleAutoRoleService autoRole)
        {
            _autoRole = autoRole;
        }

        [Command("ar_switch")]
        [Alias("автовыдача", "autoroletoggle")]
        [RequireUserPermission(GuildPermission.ManageRoles)]
        [RequireBotPermission(GuildPermission.ManageRoles)]
        public async Task ToggleAutoRole()
        {
            var enabled = _autoRole.Toggle(Context.Guild.Id);

            var embed = new EmbedBuilder()
                .WithTitle("⚙️ Автовыдача ролей")
                .WithDescription($"**Статус:** {(enabled ? "✅ Включена" : "❌ Выключена")}")
                .WithColor(enabled ? Color.Green : Color.Red)
                .WithFooter($"Команда от: {Context.User.Username}")
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("ar_add")]
        [Alias("добавить_роль")]
        [RequireUserPermission(GuildPermission.ManageRoles)]
        [RequireBotPermission(GuildPermission.ManageRoles)]
        public async Task AddAutoRole([Remainder] SocketRole role)
        {
            // Проверка иерархии ролей
            if (role.Position >= Context.Guild.CurrentUser.Hierarchy)
            {
                await ReplyAsync($"❌ Роль {role.Mention} выше моей! Я не могу её выдавать.");
                return;
            }

            var added = _autoRole.AddRole(Context.Guild.Id, role.Id);

            var embed = new EmbedBuilder()
                .WithTitle(added ? "✅ Роль добавлена" : "⚠️ Роль уже в списке")
                .WithDescription($"Роль {role.Mention} добавлена для автовыдачи")
                .WithColor(added ? Color.Green : Color.Orange)
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("ar_remove")]
        [Alias("удалить_роль")]
        [RequireUserPermission(GuildPermission.ManageRoles)]
        public async Task RemoveAutoRole([Remainder] SocketRole role)
        {
            var removed = _autoRole.RemoveRole(Context.Guild.Id, role.Id);

            var embed = new EmbedBuilder()
                .WithTitle(removed ? "✅ Роль удалена" : "⚠️ Роль не найдена")
                .WithDescription($"Роль {role.Mention} удалена из автовыдачи")
                .WithColor(removed ? Color.Green : Color.Orange)
                .Build();

            await ReplyAsync(embed: embed);
        }

        [Command("ar_status")]
        [Alias("статус_автовыдачи")]
        public async Task ShowStatus()
        {
            var enabled = _autoRole.GetStatus(Context.Guild.Id);
            var roles = _autoRole.GetRoles(Context.Guild.Id);

            var embed = new EmbedBuilder()
                .WithTitle("📊 Статус автовыдачи")
                .WithColor(enabled ? Color.Green : Color.Red)
                .AddField("Статус", enabled ? "✅ Включена" : "❌ Выключена", true);

            if (roles.Any())
            {
                var roleList = roles.Select(roleId =>
                {
                    var role = Context.Guild.GetRole(roleId);
                    return role?.Mention ?? $"<@&{roleId}>";
                });

                embed.AddField("Роли для выдачи", string.Join("\n", roleList));
            }
            else
            {
                embed.AddField("Роли для выдачи", "Нет ролей");
            }

            await ReplyAsync(embed: embed.Build());
        }

        [Command("ar_help")]
        [Alias("автовыдача_помощь")]
        public async Task ShowHelp()
        {
            var embed = new EmbedBuilder()
                .WithTitle("❓ Помощь по автовыдаче ролей")
                .WithColor(Color.Blue)
                .WithDescription("Простая система автовыдачи ролей при входе на сервер")
                .AddField("!ar_switch", "Включить/выключить автовыдачу", true)
                .AddField("!ar_add @роль", "Добавить роль для автовыдачи", true)
                .AddField("!ar_remove @роль", "Удалить роль из автовыдачи", true)
                .AddField("!ar_status", "Показать текущие настройки", true)
                .AddField("!ar_help", "Показать это сообщение", true)
                .WithFooter("Требуются права: Управление ролями")
                .Build();

            await ReplyAsync(embed: embed);
        }
    }
}

