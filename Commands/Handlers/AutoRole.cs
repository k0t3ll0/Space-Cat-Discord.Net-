using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Collections.Concurrent;


namespace Space_Cat_v3.Commands.Handlers
{
    #region Простой класс параметров
    public class SimpleAutoRole
    {
        public ulong GuildId { get; set; }
        public bool Enabled { get; set; }
        public List<ulong> RoleIds { get; set; } = new();
    }
    #endregion

    public class SimpleAutoRoleService
    {
        private readonly DiscordSocketClient _client;
        private readonly ConcurrentDictionary<ulong, SimpleAutoRole> _settings;
        private readonly string _dataFile = "autoroles.json";
        private readonly ILogger _logger;

        public SimpleAutoRoleService(DiscordSocketClient client, ILogger<SimpleAutoRoleService> logger)
        {
            _client = client;
            _settings = new ConcurrentDictionary<ulong, SimpleAutoRole>();
            _logger = logger;
            // Подписываемся на событие входа пользователя
            _client.UserJoined += OnUserJoinedAsync;

            LoadSettings();
        }

        // Загрузка настроек
        private void LoadSettings()
        {
            try
            {
                if (!File.Exists(_dataFile))
                {
                    _logger.LogInformation("Файл автовыдачи ролей не найден, создаём новый");
                    File.WriteAllText(_dataFile, "{}");
                    return;
                }

                var json = File.ReadAllText(_dataFile);
                var loaded = JsonConvert.DeserializeObject<Dictionary<ulong, SimpleAutoRole>>(json);

                if (loaded != null)
                {
                    foreach (var kvp in loaded)
                    {
                        _settings[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ошибка загрузки автовыдачи: {ex.Message}");
            }
        }

        // Сохранение настроек
        private void SaveSettings()
        {
            try
            {
                var json = JsonConvert.SerializeObject(_settings.ToDictionary(k => k.Key, v => v.Value), Formatting.Indented);
                File.WriteAllText(_dataFile, json);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ошибка сохранения автовыдачи: {ex.Message}");
            }
        }

        // Обработчик входа пользователя
        private async Task OnUserJoinedAsync(SocketGuildUser user)
        {
            try
            {
                if (!_settings.TryGetValue(user.Guild.Id, out var settings) || !settings.Enabled)
                    return;

                if (!settings.RoleIds.Any())
                    return;

                // Выдача всех ролей
                foreach (var roleId in settings.RoleIds)
                {
                    await GiveRoleAsync(user, roleId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ошибка автовыдачи: {ex.Message}");
            }
        }

        // Метод выдачи роли
        private async Task GiveRoleAsync(SocketGuildUser user, ulong roleId)
        {
            try
            {
                var role = user.Guild.GetRole(roleId);
                if (role == null)
                    return;

                // Проверка прав бота
                var botUser = user.Guild.CurrentUser;
                if (botUser.GuildPermissions.ManageRoles && role.Position < botUser.Hierarchy)
                {
                    await user.AddRoleAsync(role);
                }
            }
            catch { /* Игнорируем ошибки */ }
        }

        // ========== ПУБЛИЧНЫЕ МЕТОДЫ ==========

        // Переключить вкл/выкл
        public bool Toggle(ulong guildId)
        {
            var settings = GetOrCreateSettings(guildId);
            settings.Enabled = !settings.Enabled;
            SaveSettings();
            return settings.Enabled;
        }

        // Добавить роль
        public bool AddRole(ulong guildId, ulong roleId)
        {
            var settings = GetOrCreateSettings(guildId);

            if (settings.RoleIds.Contains(roleId))
                return false;

            settings.RoleIds.Add(roleId);
            SaveSettings();
            return true;
        }

        // Удалить роль
        public bool RemoveRole(ulong guildId, ulong roleId)
        {
            if (!_settings.TryGetValue(guildId, out var settings))
                return false;

            var removed = settings.RoleIds.Remove(roleId);
            if (removed) SaveSettings();
            return removed;
        }

        // Получить статус
        public bool GetStatus(ulong guildId)
        {
            return _settings.TryGetValue(guildId, out var settings) && settings.Enabled;
        }

        // Получить список ролей
        public List<ulong> GetRoles(ulong guildId)
        {
            return _settings.TryGetValue(guildId, out var settings)
                ? settings.RoleIds
                : new List<ulong>();
        }

        // Вспомогательный метод
        private SimpleAutoRole GetOrCreateSettings(ulong guildId)
        {
            return _settings.GetOrAdd(guildId, new SimpleAutoRole { GuildId = guildId });
        }
    }
}

