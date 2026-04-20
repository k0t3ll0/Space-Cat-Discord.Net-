using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;
using Victoria;
using Victoria.WebSocket.EventArgs;
using static Space_Cat_v3.Commands.Modules.AudioModule;
using Reason = Victoria.Enums.TrackEndReason;

namespace Space_Cat_v3.Commands.Handlers
{
    public sealed class AudioService
    {
        private readonly LavaNode<LavaPlayer<LavaTrack>, LavaTrack> _lavaNode;
        private readonly DiscordSocketClient _socketClient;
        private readonly ILogger _logger;
        public readonly HashSet<ulong> VoteQueue;
        private readonly ConcurrentDictionary<ulong, CancellationTokenSource> _disconnectTokens;
        public readonly ConcurrentDictionary<ulong, ulong> TextChannels;

        public AudioService(
            LavaNode<LavaPlayer<LavaTrack>, LavaTrack> lavaNode,
            DiscordSocketClient socketClient,
            ILogger<AudioService> logger)
        {
            _lavaNode = lavaNode;
            _socketClient = socketClient;
            _disconnectTokens = new ConcurrentDictionary<ulong, CancellationTokenSource>();
            _logger = logger;
            TextChannels = new ConcurrentDictionary<ulong, ulong>();
            VoteQueue = [];
            _lavaNode.OnWebSocketClosed += OnWebSocketClosedAsync;
            _lavaNode.OnStats += OnStatsAsync;
            _lavaNode.OnPlayerUpdate += OnPlayerUpdateAsync;
            _lavaNode.OnTrackEnd += OnTrackEndAsync;
            _lavaNode.OnTrackStart += OnTrackStartAsync;
            _lavaNode.OnTrackStuck += _lavaNode_OnTrackStuck;
        }

        private async Task _lavaNode_OnTrackStuck(TrackStuckEventArg arg)
        {
            var player = await _lavaNode.TryGetPlayerAsync(arg.GuildId);
            await player.SkipAsync(_lavaNode);
        }

        private Task OnTrackStartAsync(TrackStartEventArg arg)
        {
            return Task.CompletedTask;// SendAndLogMessageAsync(arg.GuildId, $"Сейчас играет: {arg.Track.Title}");
        }

        private async Task OnTrackEndAsync(TrackEndEventArg arg)
        {
            var guildId = arg.GuildId;
            var player = await _lavaNode.TryGetPlayerAsync(arg.GuildId);
            if (player is null) return;

            if (!GuildRepeatMode.TryGetValue(guildId, out var mode))
                mode = RepeatMode.None;

            if (mode == RepeatMode.One && arg.Track != null)
            {
                // Повтор одного трека: ставим его же в начало очереди и запускаем
                player.GetQueue().Clear(); // очищаем очередь, чтобы не мешала                
                await player.PlayAsync(_lavaNode, arg.Track!, false);
                return;
            }

            if (player.GetQueue().Count == 1)//при 0 треков в очереди, выдает ошибку в PrefixHandler(Sequence has no more elements)
            {
                // Очередь пуста – проверяем Repeat.All
                if (mode == RepeatMode.All && SavedQueues.TryGetValue(guildId, out var savedQueue) && savedQueue.Count > 0)
                {
                    // Восстанавливаем очередь из сохранённой
                    foreach (var track in savedQueue)
                        player.GetQueue().Enqueue(track);
                    
                    //Запускаем первый трек
                    if (player.GetQueue().TryDequeue(out var firstTrack))
                    {
                        await player.PlayAsync(_lavaNode, firstTrack, false);
                    }
                }
            }
            else
            {
                if (player.GetQueue().TryDequeue(out var nextTrack)) 
                    await player.PlayAsync(_lavaNode, nextTrack);
            }
        }

        private Task OnPlayerUpdateAsync(PlayerUpdateEventArg arg)
        {
            _logger.LogInformation("Пинг: {Ping}", arg.Ping);
            return Task.CompletedTask;
        }

        private Task OnStatsAsync(StatsEventArg arg)
        {
            _logger.LogInformation("{arg}", JsonSerializer.Serialize(arg));
            return Task.CompletedTask;
        }

        private Task OnWebSocketClosedAsync(WebSocketClosedEventArg arg)
        {
            _logger.LogCritical("{arg}", JsonSerializer.Serialize(arg));
            return Task.CompletedTask;
        }

        private Task SendAndLogMessageAsync(ulong guildId,
                                            string message)
        {
            _logger.LogInformation(message);
            if (!TextChannels.TryGetValue(guildId, out var textChannelId))
            {
                return Task.CompletedTask;
            }

            return (_socketClient
                    .GetGuild(guildId)
                    .GetChannel(textChannelId) as ITextChannel)!
                .SendMessageAsync(message);
        }
    }
}