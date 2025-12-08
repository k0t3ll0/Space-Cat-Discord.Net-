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
        public async Task Random( int min, int max)
        {
            var randomValue = new System.Random().Next(min, max + 1);
            await Context.Channel.SendMessageAsync(Context.User.GlobalName + " " + randomValue);
        }

        /*[Command("embed")]
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
                var task = member?.AddRoleAsync(memberRole);
                await Task.CompletedTask;
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
        }*/
    }
}
