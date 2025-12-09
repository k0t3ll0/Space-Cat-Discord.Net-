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
        public async Task DeleteAllMessages()
        {
            IEnumerable<IMessage> messages = Context.Channel.GetMessagesAsync().FlattenAsync().Result;
            await ((ITextChannel)Context.Channel).DeleteMessagesAsync(messages);
            const int delay = 3000;
            IUserMessage m = await ReplyAsync($"I have deleted {messages.Count()} messages for ya. :)");
            await Task.Delay(delay);
            await m.DeleteAsync();
        }

        [Command("help")]
        public async Task CheckAllCommands()
        {
            List<string> info = new List<string>()
            {
                "!hello - ответ от бота",
                "!embed - тестовый эмбед",
                "!clear - требует управление сообщениями(очищает все сообщения в чате в диапазоне 7 дней)",
                "!random - % of gay",
                "!rr help - помощь по настройке создания ролей",
                "!rr createpanel - создать сообщение для постановки там смайликов",
                "!rr add \"id-сообщения\" \"смайлик\" \"роль\" - добавить смайлик для получения роли",
                "!rr remove \"id-сообщения\" \"смайлик\" - убрать привязку роли к смайлику",
                "!rr info - показать общее количество привязок",
                "!rr list - показать все привязки",
                "/test - эмбед(плашка)",
                "/parameter - эмбед с текстом"
            };
            var message = new EmbedBuilder()
            {
                Title = "Команды бота",
                Description = string.Join("\r\n", info),
                Color = Color.Red
            };
            await Context.Channel.SendMessageAsync(embed: message.Build());
        }
        
    }
}
