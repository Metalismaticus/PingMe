using Vintagestory.API.Client;
using Vintagestory.API.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PingMe
{
    public class PingmeConfig
    {
        public HashSet<string> Nicknames { get; set; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public class PingmeService
    {
        readonly ICoreClientAPI capi;
        readonly ToastManager toasts;
        readonly PingmeConfig config;

        public PingmeService(ICoreClientAPI capi, ToastManager toasts, PingmeConfig config)
        {
            this.capi = capi;
            this.toasts = toasts;
            this.config = config;
        }

        public void OnSendChatMessage(int groupId, ref string message, ref EnumHandling handled)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            if (!message.StartsWith(".pingme", StringComparison.OrdinalIgnoreCase)) return;

            handled = EnumHandling.Handled;

            var parts = message.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1)
            {
                capi.ShowChatMessage("[pingme] Команды: .pingme add <ник>, .pingme show, .pingme delete <ник>");
                return;
            }

            var cmd = parts[1].ToLowerInvariant();
            switch (cmd)
            {
                case "add":
                    if (parts.Length < 3) { capi.ShowChatMessage("[pingme] Укажи ник: .pingme add <ник>"); return; }
                    var toAdd = parts[2].Trim();
                    if (toAdd.Length == 0) { capi.ShowChatMessage("[pingme] Ник пуст."); return; }
                    if (config.Nicknames.Add(toAdd))
                    {
                        capi.StoreModConfig(config, "pingme.json");
                        capi.ShowChatMessage($"[pingme] Добавлено: {toAdd}");
                    }
                    else capi.ShowChatMessage($"[pingme] Уже есть: {toAdd}");
                    break;

                case "show":
                    capi.ShowChatMessage(
                        config.Nicknames.Count == 0
                        ? "[pingme] Список пуст."
                        : "[pingme] " + string.Join(", ", config.Nicknames.OrderBy(s => s))
                    );
                    break;

                case "delete":
                case "del":
                case "remove":
                    if (parts.Length < 3) { capi.ShowChatMessage("[pingme] Укажи ник: .pingme delete <ник>"); return; }
                    var toDel = parts[2].Trim();
                    if (config.Nicknames.Remove(toDel))
                    {
                        capi.StoreModConfig(config, "pingme.json");
                        capi.ShowChatMessage($"[pingme] Удалено: {toDel}");
                    }
                    else capi.ShowChatMessage($"[pingme] Не найдено: {toDel}");
                    break;

                default:
                    capi.ShowChatMessage("[pingme] Неизвестная команда. Используй: add | show | delete");
                    break;
            }
        }

        public void OnChatMessage(int groupId, string message, EnumChatType chattype, string data)
        {
            if (string.IsNullOrEmpty(message)) return;

            // 1) Удаляем теги
            string clean = StripTags(message);

            // 2) Склеиваем переносы/табы, схлопываем лишние пробелы
            clean = OneLine(clean);

            // 3) Ограничиваем длину, чтобы не распирало тост (TwoLines в Toasts даст ровно 2 строки)
            clean = Trunc100(clean);

            // Системные: фильтруем смерти, остальное показываем
            if (chattype == EnumChatType.Notification)
            {
                if (DeathMessageFilter.IsDeathMessage(clean)) return;

                var title = GroupTitle(groupId, chattype);
                toasts.Enqueue(title, clean);
                return;
            }

            // Обычный чат: фильтр по прозвищам
            if (!ContainsAnyNicknameWordBounded(clean)) return;

            ParseAuthorAndBody(clean, out string author, out string body);

            // Игнорируем свои
            var myName = capi?.World?.Player?.PlayerName;
            if (!string.IsNullOrEmpty(author) && !string.IsNullOrEmpty(myName) &&
                author.Equals(myName, StringComparison.OrdinalIgnoreCase)) return;

            // Автор в первую строку (в Toasts это отдельная строка), тело — во вторую
            if (!string.IsNullOrEmpty(author)) author += ":";

            var chatTitle = GroupTitle(groupId, chattype);
            toasts.EnqueueChat(chatTitle, author, Trunc100(OneLine(body)));
        }

        // --- helpers ---

        static string StripTags(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            return Regex.Replace(text, @"</?\w+[^>]*>", "", RegexOptions.CultureInvariant);
        }

        static string OneLine(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            s = s.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");
            return Regex.Replace(s, @"\s{2,}", " ");
        }

        static string Trunc100(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            const int max = 100;
            if (s.Length <= max) return s;
            return s.Substring(0, max - 1) + "…";
        }

        bool ContainsAnyNicknameWordBounded(string message)
        {
            if (config.Nicknames == null || config.Nicknames.Count == 0) return false;

            foreach (var nick in config.Nicknames.OrderByDescending(n => n.Length))
            {
                if (string.IsNullOrWhiteSpace(nick)) continue;
                if (ContainsWordLike(message, nick)) return true;
            }
            return false;
        }

        static bool ContainsWordLike(string text, string needle)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(needle)) return false;
            var pattern = $@"(?<![\p{{L}}\p{{N}}_]){Regex.Escape(needle)}(?![\p{{L}}\p{{N}}_])";
            return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        static void ParseAuthorAndBody(string message, out string author, out string body)
        {
            author = null;
            body = message ?? "";

            var m1 = Regex.Match(body, @"^\s*<([^>]+)>\s*(.*)$");
            if (m1.Success) { author = m1.Groups[1].Value.Trim(); body = m1.Groups[2].Value; return; }

            var m2 = Regex.Match(body, @"^\s*([^:]{1,64}):\s*(.*)$");
            if (m2.Success) { author = m2.Groups[1].Value.Trim(); body = m2.Groups[2].Value; }
        }

        string GroupTitle(int groupId, EnumChatType chatType)
        {
            try
            {
                var player = capi?.World?.Player;
                if (player != null)
                {
                    // 1) Прямой поиск по членству
                    var membership = player.GetGroup(groupId);
                    var name = GetGroupNameReflect(membership);
                    if (!string.IsNullOrWhiteSpace(name))
                        return name;

                    // 2) Перебор всех групп игрока без жёсткого доступа к GroupId
                    var groups = player.Groups;
                    if (groups != null)
                    {
                        foreach (var g in groups)
                        {
                            int? gid = GetGroupIdReflect(g);
                            if (gid.HasValue && gid.Value == groupId)
                            {
                                name = GetGroupNameReflect(g);
                                if (!string.IsNullOrWhiteSpace(name))
                                    return name;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                capi.Logger.Error($"[PingMe] Ошибка при получении названия группы {groupId}: {ex}");
            }

            // Фолбэк
            if (chatType == EnumChatType.Notification) return "System";
            if (groupId == 0) return "General chat";
            return $"Group {groupId}";
        }

        static string GetGroupNameReflect(object membership)
        {
            if (membership == null) return null;
            var t = membership.GetType();
            var p = t.GetProperty("GroupName") ?? t.GetProperty("Name") ?? t.GetProperty("Title");
            return p?.GetValue(membership)?.ToString();
        }

        static int? GetGroupIdReflect(object membership)
        {
            if (membership == null) return null;
            var t = membership.GetType();
            var p = t.GetProperty("GroupId") ?? t.GetProperty("Id") ?? t.GetProperty("GroupID");
            if (p == null) return null;
            var val = p.GetValue(membership);
            if (val is int i) return i;
            if (val is long l) return (int)l;
            if (val != null && int.TryParse(val.ToString(), out var j)) return j;
            return null;
        }
    }
}
