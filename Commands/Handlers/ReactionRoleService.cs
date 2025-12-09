using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

public class ReactionRoleService : IDisposable
{
    #region Модели данных
    public class ReactionRoleBinding
    {
        public ulong MessageId { get; set; }
        public ulong ChannelId { get; set; }
        public ulong GuildId { get; set; }
        public string Emote { get; set; }
        public ulong RoleId { get; set; }
        public bool RemoveOnUnreact { get; set; } = true;
        public bool IsUnique { get; set; } = false;
        public string UniqueGroup { get; set; } = null;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public ulong CreatedBy { get; set; }
    }

    public class GuildBindings
    {
        public ulong GuildId { get; set; }
        public List<ReactionRoleBinding> Bindings { get; set; } = new();
        public bool Enabled { get; set; } = true;
    }
    #endregion

    #region Поля и свойства
    private readonly ConcurrentDictionary<ulong, GuildBindings> _guildBindings;
    private readonly DiscordSocketClient _client;
    private readonly ILogger<ReactionRoleService> _logger;
    private readonly string _storagePath = "reaction_roles.json";
    private bool _isInitialized = false;
    private bool _disposed = false;
    private DateTime _lastSaveTime = DateTime.MinValue;

    public int TotalBindings => _guildBindings.Values.Sum(g => g.Bindings.Count);
    public int TotalGuilds => _guildBindings.Count;
    #endregion

    #region Конструктор и инициализация
    public ReactionRoleService(DiscordSocketClient client, ILogger<ReactionRoleService> logger = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger;
        _guildBindings = new ConcurrentDictionary<ulong, GuildBindings>();

        _logger?.LogInformation("ReactionRoleService создан");
         InitializeAsync();
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized)
            return;

        try
        {
            LoadBindings();
            SubscribeToEvents();
            _isInitialized = true;

            _logger?.LogInformation("ReactionRoleService инициализирован. Загружено {GuildCount} серверов, {BindingCount} привязок",
                TotalGuilds, TotalBindings);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ошибка при инициализации ReactionRoleService");
            throw;
        }
    }

    private void SubscribeToEvents()
    {
        _client.ReactionAdded += HandleReactionAddedAsync;
        _client.ReactionRemoved += HandleReactionRemovedAsync;
        _client.MessageDeleted += HandleMessageDeletedAsync;
        _client.LeftGuild += HandleLeftGuildAsync;
    }
    #endregion

    #region Сохранение и загрузка данных
    private void LoadBindings()
    {
        try
        {
            if (!File.Exists(_storagePath))
            {
                _logger?.LogInformation("Файл с привязками не найден. Будет создан новый.");
                return;
            }

            var json = File.ReadAllText(_storagePath);
            var savedData = JsonConvert.DeserializeObject<Dictionary<ulong, GuildBindings>>(json);

            if (savedData != null)
            {
                foreach (var kvp in savedData)
                {
                    _guildBindings[kvp.Key] = kvp.Value;
                }
            }

            _logger?.LogInformation("Загружено {Count} привязок из файла", TotalBindings);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ошибка при загрузке привязок из файла");
            // Создаем пустой файл при ошибке
            SaveBindings();
        }
    }

    private void SaveBindings()
    {
        try
        {
            var json = JsonConvert.SerializeObject(_guildBindings, Formatting.Indented);
            File.WriteAllText(_storagePath, json);
            _lastSaveTime = DateTime.Now;

            _logger?.LogDebug("Привязки сохранены в файл");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ошибка при сохранении привязок в файл");
        }
    }

    private async Task SaveBindingsAsync()
    {
        await Task.Run(() => SaveBindings());
    }
    #endregion

    #region Управление привязками (публичные методы)
    public async Task<ReactionRoleBinding> AddBindingAsync(ReactionRoleBinding binding)
    {
        ValidateBinding(binding);

        var guildBindings = _guildBindings.GetOrAdd(binding.GuildId,
            new GuildBindings { GuildId = binding.GuildId });

        // Проверка на существующую привязку
        if (guildBindings.Bindings.Any(b =>
            b.MessageId == binding.MessageId && b.Emote == binding.Emote))
        {
            throw new InvalidOperationException("Привязка уже существует");
        }

        guildBindings.Bindings.Add(binding);
        await SaveBindingsAsync();

        _logger?.LogInformation("Добавлена привязка: Guild={GuildId}, Message={MessageId}, Emote={Emote}, Role={RoleId}",
            binding.GuildId, binding.MessageId, binding.Emote, binding.RoleId);

        return binding;
    }

    public async Task<bool> RemoveBindingAsync(ulong guildId, ulong messageId, string emote)
    {
        if (!_guildBindings.TryGetValue(guildId, out var guildBindings))
            return false;

        var binding = guildBindings.Bindings.FirstOrDefault(b =>
            b.MessageId == messageId && b.Emote == emote);

        if (binding == null)
            return false;

        guildBindings.Bindings.Remove(binding);

        if (guildBindings.Bindings.Count == 0)
        {
            _guildBindings.TryRemove(guildId, out _);
        }

        await SaveBindingsAsync();
        _logger?.LogInformation("Удалена привязка: Guild={GuildId}, Message={MessageId}, Emote={Emote}",
            guildId, messageId, emote);

        return true;
    }

    public async Task<int> RemoveAllBindingsForGuildAsync(ulong guildId)
    {
        if (!_guildBindings.TryGetValue(guildId, out var guildBindings))
            return 0;

        var count = guildBindings.Bindings.Count;
        _guildBindings.TryRemove(guildId, out _);

        await SaveBindingsAsync();
        _logger?.LogInformation("Удалены все привязки ({Count}) для гильдии {GuildId}", count, guildId);

        return count;
    }

    public ReactionRoleBinding GetBinding(ulong guildId, ulong messageId, string emote)
    {
        return _guildBindings.TryGetValue(guildId, out var guildBindings)
            ? guildBindings.Bindings.FirstOrDefault(b =>
                b.MessageId == messageId && b.Emote == emote)
            : null;
    }

    public List<ReactionRoleBinding> GetGuildBindings(ulong guildId)
    {
        return _guildBindings.TryGetValue(guildId, out var guildBindings)
            ? new List<ReactionRoleBinding>(guildBindings.Bindings)
            : new List<ReactionRoleBinding>();
    }

    public bool IsGuildEnabled(ulong guildId)
    {
        return !_guildBindings.TryGetValue(guildId, out var guildBindings) || guildBindings.Enabled;
    }

    public async Task SetGuildEnabledAsync(ulong guildId, bool enabled)
    {
        var guildBindings = _guildBindings.GetOrAdd(guildId, new GuildBindings { GuildId = guildId });
        guildBindings.Enabled = enabled;
        await SaveBindingsAsync();
    }
    #endregion

    #region Обработчики событий (внутренние)
    private async Task HandleReactionAddedAsync(
        Cacheable<IUserMessage, ulong> cachedMessage,
        Cacheable<IMessageChannel, ulong> cachedChannel,
        SocketReaction reaction)
    {
        try
        {
            if (reaction.User.Value?.IsBot ?? true)
                return;

            var message = await cachedMessage.GetOrDownloadAsync();
            if (message == null)
                return;

            var channel = await cachedChannel.GetOrDownloadAsync();
            if (channel is not SocketGuildChannel guildChannel)
                return;

            var guild = guildChannel.Guild;
            var user = guild.GetUser(reaction.UserId);

            if (user == null || !IsGuildEnabled(guild.Id))
                return;

            string emoteString = GetEmoteString(reaction.Emote);
            var binding = GetBinding(guild.Id, message.Id, emoteString);

            if (binding == null)
                return;

            var role = guild.GetRole(binding.RoleId);
            if (role == null)
            {
                await RemoveBindingAsync(guild.Id, message.Id, emoteString);
                return;
            }

            if (!user.Roles.Any(r => r.Id == role.Id))
            {
                await user.AddRoleAsync(role);
                _logger?.LogInformation("Роль выдана: User={User}, Role={Role}, Guild={Guild}",
                    user.Username, role.Name, guild.Name);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ошибка при обработке добавления реакции");
        }
    }

    private async Task HandleReactionRemovedAsync(
        Cacheable<IUserMessage, ulong> cachedMessage,
        Cacheable<IMessageChannel, ulong> cachedChannel,
        SocketReaction reaction)
    {
        try
        {
            if (reaction.User.Value?.IsBot ?? true)
                return;

            var message = await cachedMessage.GetOrDownloadAsync();
            if (message == null)
                return;

            var channel = await cachedChannel.GetOrDownloadAsync();
            if (channel is not SocketGuildChannel guildChannel)
                return;

            var guild = guildChannel.Guild;
            var user = guild.GetUser(reaction.UserId);

            if (user == null || !IsGuildEnabled(guild.Id))
                return;

            string emoteString = GetEmoteString(reaction.Emote);
            var binding = GetBinding(guild.Id, message.Id, emoteString);

            if (binding == null || !binding.RemoveOnUnreact)
                return;

            var role = guild.GetRole(binding.RoleId);
            if (role == null)
                return;

            if (user.Roles.Any(r => r.Id == role.Id))
            {
                await user.RemoveRoleAsync(role);
                _logger?.LogInformation("Роль забрана: User={User}, Role={Role}, Guild={Guild}",
                    user.Username, role.Name, guild.Name);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ошибка при обработке удаления реакции");
        }
    }

    private async Task HandleMessageDeletedAsync(
        Cacheable<IMessage, ulong> cachedMessage,  // Изменилось на IMessage вместо IMessageChannel
        Cacheable<IMessageChannel, ulong> cachedChannel)
    {
        try
        {
            var messageId = cachedMessage.Id;  // Получаем ID из первого параметра

            var channel = await cachedChannel.GetOrDownloadAsync();
            if (channel is not SocketGuildChannel guildChannel)
                return;

            var bindings = GetGuildBindings(guildChannel.Guild.Id);
            var bindingsToRemove = bindings.Where(b => b.MessageId == messageId).ToList();

            foreach (var binding in bindingsToRemove)
            {
                await RemoveBindingAsync(guildChannel.Guild.Id, binding.MessageId, binding.Emote);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ошибка при обработке удаления сообщения");
        }
    }

    // Метод HandleLeftGuildAsync - правильная сигнатура
    private async Task HandleLeftGuildAsync(SocketGuild guild)
    {
        try
        {
            await RemoveAllBindingsForGuildAsync(guild.Id);
            _logger?.LogInformation("Удалены все привязки для гильдии {GuildName} (бот вышел)", guild.Name);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Ошибка при обработке выхода из гильдии");
        }
    }
    #endregion

    #region Вспомогательные методы
    private string GetEmoteString(IEmote emote)
    {
        return emote switch
        {
            Emote e => e.ToString(),
            Emoji e => e.Name,
            _ => emote.ToString()
        };
    }

    private void ValidateBinding(ReactionRoleBinding binding)
    {
        if (binding == null) throw new ArgumentNullException(nameof(binding));
        if (binding.MessageId == 0) throw new ArgumentException("MessageId не может быть 0");
        if (binding.RoleId == 0) throw new ArgumentException("RoleId не может быть 0");
        if (string.IsNullOrWhiteSpace(binding.Emote)) throw new ArgumentException("Emote не может быть пустым");
        if (binding.GuildId == 0) throw new ArgumentException("GuildId не может быть 0");
    }
    #endregion

    #region IDisposable
    public void Dispose()
    {
        if (!_disposed)
        {
            _client.ReactionAdded -= HandleReactionAddedAsync;
            _client.ReactionRemoved -= HandleReactionRemovedAsync;
            _client.MessageDeleted -= HandleMessageDeletedAsync;
            _client.LeftGuild -= HandleLeftGuildAsync;

            // Сохраняем данные при завершении
            SaveBindings();

            _disposed = true;
            _logger?.LogInformation("ReactionRoleService остановлен");
        }
    }
    #endregion
}