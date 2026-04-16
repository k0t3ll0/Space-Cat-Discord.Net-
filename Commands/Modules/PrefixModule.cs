using Discord;
using Discord.Commands;

namespace Space_Cat_v3.Commands.Modules
{
    public class PrefixModule : ModuleBase<SocketCommandContext>
    {
        [Command("hello")]
        //Создание асинхронной задачи, в параметры идёт контекст
        public async Task SendHello()
        {
            await Context.Message.ReplyAsync("kuku");
        }

        [Command("random")]
        [Summary("% of gay")]
        public async Task Random(int min, int max)
        {
            var randomValue = new System.Random().Next(min, max + 1);
            await Context.Channel.SendMessageAsync(Context.User.GlobalName + " " + randomValue);
        }

        [Command("embed")]
        public async Task Embed()
        {
            //Эмбед - сообщение в виде плашки
            //Задаётся Заголовок - Title, Описание, Цвет линии сбоку
            var message = new EmbedBuilder()
            {
                Title = "Первый эмбед",
                Description = $"Выполнен по запросу {Context.User.GlobalName}",
                Color = Color.Magenta
            };

            await Context.Channel.SendMessageAsync(embed: message.Build());
        }
        [Command("role")]
        public async Task GetRole()
        {

            if (Context.Channel.Name == "verify")
            {
                var roles = Context.Guild.Roles;
                var memberRole = roles.First(x => x.Name == "member");
                IGuildUser member = (IGuildUser)Context.User;
                await member?.AddRoleAsync(memberRole)!;
            }

        }
        [Command("clear")]
        [RequireUserPermission(GuildPermission.ManageMessages)]
        [RequireBotPermission(GuildPermission.ManageMessages)]
        public async Task DeleteAllMessagesAsync()
        {
            try
            {
                var channel = (ITextChannel)Context.Channel;

                // Получаем все сообщения в канале
                var messages = await channel.GetMessagesAsync(1000).FlattenAsync();

                // Фильтруем: только те, что не старше 14 дней (и не старше 14 дней от текущего момента)
                var cutoff = DateTimeOffset.UtcNow.AddDays(-14);
                var recentMessages = messages.Where(m => m.CreatedAt > cutoff).ToList();

                if (!recentMessages.Any())
                {
                    await ReplyAsync("Нет сообщений младше 14 дней для удаления.");
                    return;
                }

                // Discord позволяет удалить не более 100 сообщений за раз
                var chunks = recentMessages.Chunk(100);
                int totalDeleted = 0;

                foreach (var chunk in chunks)
                {
                    await channel.DeleteMessagesAsync(chunk);
                    totalDeleted += chunk.Length;
                    await Task.Delay(1000); // небольшая задержка, чтобы не словить rate limit
                }

                var reply = await ReplyAsync($"Удалено {totalDeleted} сообщений.");

                // Удаляем своё сообщение через 3 секунды
                await Task.Delay(3000);
                await reply.DeleteAsync();
            }
            catch (ArgumentOutOfRangeException)
            {
                await ReplyAsync("Нет сообщений для удаления.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка в clear: {ex.Message}");
                await ReplyAsync("❌ Произошла ошибка при удалении сообщений.");
            }
        }

        [Command("help")]
        [Alias ("h")]
        public async Task CheckAllCommands()
        {
            List<string> info = File.ReadAllLines("commands.txt").ToList();
            var embed = new EmbedBuilder()
            {
                Title = "Команды бота",
                Description = string.Join("\n", info),
                Color = Color.Blue,
            };
            await ReplyAsync(embed: embed.Build());
        }

    }
}
