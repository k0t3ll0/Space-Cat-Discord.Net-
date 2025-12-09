using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Lavalink4NET.Players;
using Space_Cat_v3.Commands.Handlers;
using Victoria;
using Victoria.Rest.Search;

namespace Space_Cat_v3.Commands.Modules;

public sealed class AudioModule(
    LavaNode<LavaPlayer<LavaTrack>, LavaTrack> lavaNode,
    AudioService audioService)
    : ModuleBase<SocketCommandContext>
{
    private static readonly IEnumerable<int> Range = Enumerable.Range(1900, 2000);

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
        var voiceChannel = (Context.User as IVoiceState).VoiceChannel;
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
        if (string.IsNullOrWhiteSpace(searchQuery))
        {
            await ReplyAsync("Введите ссылку на музыку.");
            return;
        }
        if ((Context.Guild.CurrentUser as IVoiceState).VoiceChannel is null)
            await JoinAsync();
        var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
        if (player == null)
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

        var searchResponse = await lavaNode.LoadTrackAsync(searchQuery);
        if (searchResponse.Type is SearchType.Empty or SearchType.Error)
        {
            await ReplyAsync($"Не могу найти ничего по запросу: `{searchQuery}`.");
            return;
        }

        var track = searchResponse.Tracks.FirstOrDefault();
        if (player.GetQueue().Count == 0)
        {
            await player.PlayAsync(lavaNode, track);
            await ReplyAsync($"Now playing: {track.Title}");
            return;
        }

        player.GetQueue().Enqueue(track);
        await ReplyAsync($"Добавил {track.Title} в очередь.");
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
            await ReplyAsync("Больше нечего проигрывать.");
        }
        catch (Exception exception)
        {
            await ReplyAsync(exception.Message);
        }
    }

    [Command("skip"), RequirePlayer]
    public async Task SkipAsync()
    {
        var player = await lavaNode.TryGetPlayerAsync(Context.Guild.Id);
        if (!player.State.IsConnected)
        {
            await ReplyAsync("Воу погоди, не могу пропустить когда ничего нету.");
            return;
        }

        var voiceChannelUsers = Context.Guild.CurrentUser.VoiceChannel
            .Users
            .Where(x => !x.IsBot)
            .ToArray();

        if (!audioService.VoteQueue.Add(Context.User.Id))
        {
            await ReplyAsync("Вы можете проголосовать снова.");
            return;
        }

        var percentage = audioService.VoteQueue.Count / voiceChannelUsers.Length * 100;
        if (percentage < 85)
        {
            await ReplyAsync("Вам нужно больше 85% голосов для пропуска.");
            return;
        }

        try
        {
            var (skipped, currenTrack) = await player.SkipAsync(lavaNode);
            await ReplyAsync($"Пропущен: {skipped.Title}\nТекущий: {currenTrack.Title}");
        }
        catch (Exception exception)
        {
            await ReplyAsync(exception.Message);
        }
    }
}