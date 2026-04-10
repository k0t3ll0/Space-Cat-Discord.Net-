using Discord;
using Discord.Commands;
using Space_Cat_v3.Commands.Handlers;
using System.Text;
using Victoria;
using Victoria.Rest.Search;

namespace Space_Cat_v3.Commands.Modules;

public sealed class AudioModule(
    LavaNode<LavaPlayer<LavaTrack>, LavaTrack> lavaNode,
    AudioService audioService) : ModuleBase<SocketCommandContext>
{
    [Command("join")]
    public async Task JoinAsync()
    {
        var voiceState = Context.User as IVoiceState;
        if (voiceState?.VoiceChannel == null)
        {
            await ReplyAsync("Вы должны быть подключены к каналу!");
            return;
        }
        try
        {
            await lavaNode.JoinAsync(voiceState.VoiceChannel);
            await ReplyAsync($"Joined {voiceState.VoiceChannel.Name}!");
            audioService.TextChannels.TryAdd(Context.Guild.Id, Context.Channel.Id);
        }
        catch (Exception exception)
        {
            await ReplyAsync(exception.ToString());
        }
    }
    [Command("leave")]
    public async Task LeaveAsync()
    {
        var voiceChannel = ((IVoiceState)Context.User).VoiceChannel;
        if (voiceChannel == null)
        {
            await ReplyAsync("❌ Вы должны быть в голосовом каналу!");
            return;
        }
        try
        {
            await lavaNode.LeaveAsync(voiceChannel);
            await ReplyAsync($"Я покинул {voiceChannel.Name}!");
        }
        catch (Exception exception)
        {
            await ReplyAsync(exception.Message);
        }
    }
    [Command("play")]
    public async Task PlayAsync([Remainder] string searchQuery)
    {
        // 1. Проверка голосового канала и подключение...
        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            await ReplyAsync("Введите ссылку на музыку.");
            return;
        }
        if ((Context.Guild.CurrentUser as IVoiceState).VoiceChannel is null)
            await JoinAsync();
        var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
        if (player is null)
        {
            var voiceState = Context.User as IVoiceState;
            if (voiceState?.VoiceChannel == null)
            {
                await ReplyAsync("Вы должны быть подключены к голосовому каналу!");
                return;
            }

            try
            {
                player = await lavaNode.JoinAsync(voiceState.VoiceChannel);
                await ReplyAsync($"Подключился {voiceState.VoiceChannel.Name}!");
                audioService.TextChannels.TryAdd(Context.Guild.Id, Context.Channel.Id);
            }
            catch (Exception exception)
            {
                await ReplyAsync(exception.Message);
            }
        }

        // 2. Загружаем треки
        var loadResult = await lavaNode.LoadTrackAsync(searchQuery);

        // 3. Обрабатываем результат
        switch (loadResult.Type)
        {
            case SearchType.Track:
                // Один трек
                await PlayTrackAsync(player, loadResult.Tracks.First());
                break;

            case SearchType.Playlist:
                // Плейлист – добавляем все треки в очередь
                var playlist = loadResult.Playlist;
                var tracks = loadResult.Tracks.ToList();

                foreach (var track in tracks)
                {
                    player.GetQueue().Enqueue(track);
                }
                // Если ничего не играет – запускаем первый трек
                if (player!.Track == null)
                {
                    await player.SkipAsync(lavaNode);
                    await player.PlayAsync(lavaNode, tracks.First()); 
                    await ReplyAsync($"🎶 Сейчас играет: {tracks.First().Title}\n📋 Добавлено {tracks.Count} треков из плейлиста `{playlist.Name}`");
                }
                else
                {
                    await ReplyAsync($"📋 Добавлено {tracks.Count} треков из плейлиста `{playlist.Name}` в очередь.");
                }
                break;

            case SearchType.Search:
                // Результат поиска – берём первый трек
                var searchTrack = loadResult.Tracks.First();
                await PlayTrackAsync(player, searchTrack);
                break;

            case SearchType.Empty:
                await ReplyAsync($"Ничего не нашлось по запросу {searchQuery}");
                break;
            case SearchType.Error:
                await ReplyAsync($"❌ Не удалось загрузить: {searchQuery}");
                break;

        }
    }

    private async Task PlayTrackAsync(LavaPlayer<LavaTrack> player, LavaTrack track)
    {
        if (player.Track is not null)
        {
            player.GetQueue().Enqueue(track);
            await ReplyAsync($"➕ Добавлено в очередь: {track.Title}");
        }
        else
        {
            await player.PlayAsync(lavaNode, track);
            await ReplyAsync($"🎶 Сейчас играет: {track.Title}");
        }
    }

    [Command("pause"), RequirePlayer]
    public async Task PauseAsync()
    {
        var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
        if (player.IsPaused && player.Track != null)
        {
            await ReplyAsync("Нельзя поставить паузу когда ничего не играет!");
            return;
        }

        try
        {
            await player.PauseAsync(lavaNode);
            await ReplyAsync($"На паузе: {player.Track.Title}");
        }
        catch (Exception exception)
        {
            await ReplyAsync(exception.Message);
        }
    }

    [Command("resume"), RequirePlayer]
    public async Task ResumeAsync()
    {
        var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
        if (!player.IsPaused && player.Track != null)
        {
            await ReplyAsync("Нельзя возобновить когда ничего не играет!");
            return;
        }

        try
        {
            await player.ResumeAsync(lavaNode, player.Track);
            await ReplyAsync($"Возобновил: {player.Track.Title}");
        }
        catch (Exception exception)
        {
            await ReplyAsync(exception.Message);
        }
    }

    [Command("stop"), RequirePlayer]
    public async Task StopAsync()
    {
        var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
        if (!player.State.IsConnected || player.Track == null)
        {
            await ReplyAsync("Воу, не могу остановить то чего нет.");
            return;
        }

        try
        {
            await player.StopAsync(lavaNode, player.Track);
            await LeaveAsync();
        }
        catch (Exception exception)
        {
            await ReplyAsync(exception.Message);
        }
    }

    [Command("next"), RequirePlayer]
    public async Task SkipAsync()
    {
        var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
        if (player?.Track == null)
        {
            await ReplyAsync("❌ Сейчас ничего не играет.");
            return;
        }
        var oldTrack = player.Track;
        var nextTrack = player.GetQueue().First();
        await player.PlayAsync(lavaNode, nextTrack, false);
        await ReplyAsync($"⏭ Пропущен: **{oldTrack.Title}**\n▶ Теперь играет: **{nextTrack.Title}**");


    }
    [Command("volume")]
    public async Task ChangeVolume(string volume)
    {
        if (!string.IsNullOrWhiteSpace(volume))
        {
            try
            {
                var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
                if (player is not null)
                {
                    await player.SetVolumeAsync(lavaNode, int.Parse(volume));
                    await ReplyAsync($"Текущая громкость: {volume}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Ошибка изменения громкости: {ex.Message}");
            }
        }

    }
    [Command("playlist")]
    public async Task CheckQueue()
    {
        var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
        var queue = player.GetQueue();
        var currentTrack = player.Track;

        if (queue.Count == 0 && currentTrack == null)
        {
            await ReplyAsync("📭 Очередь пуста.");
            return;
        }

        var description = new StringBuilder();

        if (currentTrack != null)
            description.AppendLine($"🎵 **Сейчас играет:** {currentTrack.Title}");

        if (queue.Count > 0)
        {
            description.AppendLine("\n**В очереди:**");
            int index = 1;
            foreach (var track in queue.Take(10)) // ограничим вывод первыми 10 треками
            {
                description.AppendLine($"{index++}. {track.Title}");
            }

            if (queue.Count > 10)
                description.AppendLine($"... и ещё {queue.Count - 10} треков.");
        }

        var embed = new EmbedBuilder()
            .WithTitle("🎧 Очередь воспроизведения")
            .WithDescription(description.ToString())
            .WithColor(Color.Blue)
            .Build();

        await ReplyAsync(embed: embed);
    }

}
