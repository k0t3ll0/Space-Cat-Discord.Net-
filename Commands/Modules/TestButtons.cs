using Discord;
using Discord.Interactions;

namespace Space_Cat_v3.Commands.Modules
{
    public class TestButtonsModule : InteractionModuleBase<SocketInteractionContext>
    {
        [SlashCommand("test-buttons", "Создать тестовые кнопки")]
        public async Task CreateTestButtonsAsync()
        {
            var embed = new EmbedBuilder()
                .WithTitle("Тестовые кнопки")
                .WithDescription("Нажмите кнопку для проверки")
                .WithColor(Color.Blue)
                .Build();

            var buttons = new ComponentBuilder()
                .WithButton("Тестовая кнопка 1", "test_button_1", ButtonStyle.Primary)
                .WithButton("Тестовая кнопка 2", "test_button_2", ButtonStyle.Success)
                .WithButton("Опасная кнопка", "danger_button", ButtonStyle.Danger);

            await RespondAsync(embed: embed, components: buttons.Build());
        }

        // Обработчик для кнопки test_button_1
        [ComponentInteraction("test_button_1")]
        public async Task HandleTestButton1Async()
        {
            await RespondAsync("✅ Кнопка 1 нажата!", ephemeral: true);
        }

        // Обработчик для кнопки test_button_2
        [ComponentInteraction("test_button_2")]
        public async Task HandleTestButton2Async()
        {
            await DeferAsync(ephemeral: true);
            await Task.Delay(1000); // Имитация долгой операции
            await FollowupAsync("✅ Кнопка 2 обработана с задержкой!", ephemeral: true);
        }

        // Обработчик для danger_button
        [ComponentInteraction("danger_button")]
        public async Task HandleDangerButtonAsync()
        {
            var embed = new EmbedBuilder()
                .WithTitle("⚠️ Внимание!")
                .WithDescription("Вы нажали опасную кнопку")
                .WithColor(Color.Red)
                .Build();

            await RespondAsync(embed: embed, ephemeral: true);
        }
    }
}