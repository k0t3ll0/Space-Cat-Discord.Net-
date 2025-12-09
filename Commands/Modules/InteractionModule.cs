using Discord;
using Discord.Interactions;


namespace Space_Cat_v3.Commands.Modules
{
    public class InteractionModule: InteractionModuleBase<SocketInteractionContext>
    {
        /*[SlashCommand("test", "Простая слэш команда")]
        public async Task TestSlashCommand()
        { 
            await DeferAsync();
            var message = new EmbedBuilder()
            {
                Title = "Первый эмбед",
                Description = $"Выполнен по запросу {Context?.User.GlobalName}",
                Color = Color.Blue
            };
            ComponentBuilder builder = new();


            //Добавление в ответ чего-либо.
            await ModifyOriginalResponseAsync(msg => msg.Embed = message.Build());
        }

        [SlashCommand("parameter", "Тестовая команда с параметром")]
        public async Task TestSlashCommandParameter(string testParameter)
        {
            await DeferAsync();
            var message = new EmbedBuilder()
            {
                Title = "Тестовая эмбед с параметром",
                Description = $"Тестовая слэш-команда с параметром {testParameter}",
                Color = Color.Red
            };
            await ModifyOriginalResponseAsync(msg => msg.Embed = message.Build());
        }
        [SlashCommand("ping", "Pong!")]
        public async Task Ping() => await RespondAsync("pong");*/


    }
}
