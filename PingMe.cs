using Vintagestory.API.Client;
using Vintagestory.API.Common;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace PingMe
{
    public class PingmeConfig
    {
        public HashSet<string> Nicknames { get; set; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Options
        public bool SoundEnabled { get; set; } = true;
        public bool SystemNotifications { get; set; } = true;   // show all system chats
        public float ToastStaySeconds { get; set; } = 3.8f;     // toast duration

        // System notifications pass-through prefixes (always allowed even if SystemNotifications==false)
        public List<string> SystemPassPrefixes { get; set; } =
            new List<string>();   // пусто по умолчанию
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
                capi.ShowChatMessage("[pingme] Commands:");
                capi.ShowChatMessage(".pingme add <nick>      — add a nickname");
                capi.ShowChatMessage(".pingme show            — show the nickname list");
                capi.ShowChatMessage(".pingme delete <nick>   — delete a nickname");
                capi.ShowChatMessage(".pingme sound           — toggle sound on/off");
                capi.ShowChatMessage(".pingme system          — toggle all system notifications on/off");
                capi.ShowChatMessage(".pingme time <sec>      — toast duration");
                capi.ShowChatMessage(".pingme sysprefix list  — list of system-message skip prefixes");
                capi.ShowChatMessage(".pingme sysprefix add <prefix>   — add a prefix");
                capi.ShowChatMessage(".pingme sysprefix del <prefix>   — delete a prefix");
                capi.ShowChatMessage(".pingme sysprefix clear         — clear the list");
                return;
            }

            var cmd = parts[1].ToLowerInvariant();
            switch (cmd)
            {
                case "add":
                {
                    if (parts.Length < 3) { capi.ShowChatMessage("[pingme] Provide a nickname: .pingme add <nick>"); return; }
                    var toAdd = parts[2].Trim();
                    if (toAdd.Length == 0) { capi.ShowChatMessage("[pingme] Nickname is empty."); return; }
                    if (config.Nicknames.Add(toAdd))
                    {
                        capi.StoreModConfig(config, "pingme.json");
                        capi.ShowChatMessage($"[pingme] Added: {toAdd}");
                    }
                    else capi.ShowChatMessage($"[pingme] Already exists: {toAdd}");
                    break;
                }

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
                {
                    if (parts.Length < 3) { capi.ShowChatMessage("[pingme] Provide a nickname: .pingme delete <nick>"); return; }
                    var toDel = parts[2].Trim();
                    if (config.Nicknames.Remove(toDel))
                    {
                        capi.StoreModConfig(config, "pingme.json");
                        capi.ShowChatMessage($"[pingme] Удалён: {toDel}");
                    }
                    else capi.ShowChatMessage($"[pingme] Не найден: {toDel}");
                    break;
                }

                case "sound":
                    config.SoundEnabled = !config.SoundEnabled;
                    capi.StoreModConfig(config, "pingme.json");
                    capi.ShowChatMessage($"[pingme] Sound: {(config.SoundEnabled ? "on" : "off")}");
                    break;

                case "system":
                    config.SystemNotifications = !config.SystemNotifications;
                    capi.StoreModConfig(config, "pingme.json");
                    capi.ShowChatMessage($"[pingme] System: {(config.SystemNotifications ? "on (all)" : "off (Except for prefix-based ones)")}");
                    break;

                case "time":
                {
                    if (parts.Length < 3)
                    {
                        capi.ShowChatMessage($"[pingme] Сейчас: {config.ToastStaySeconds:0.##} sec. Example: .pingme time 5");
                        return;
                    }
                    var raw = parts[2].Trim().Replace(',', '.');
                    if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float secs))
                    {
                        secs = Math.Max(0.5f, Math.Min(30f, secs));
                        config.ToastStaySeconds = secs;
                        capi.StoreModConfig(config, "pingme.json");
                        capi.ShowChatMessage($"[pingme] Showtime: {secs:0.##} сек");
                    }
                    else
                    {
                        capi.ShowChatMessage("[pingme] Invalid number. Example: .pingme time 4.5");
                    }
                    break;
                }

                case "sysprefix":
                case "sp":
                {
                    if (parts.Length < 3)
                    {
                        capi.ShowChatMessage("[pingme] Usage: .pingme sysprefix <list|add <p>|del <p>|clear>");
                        return;
                    }
                    var rest = parts[2];
                    var subparts = rest.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                    var sub = subparts[0].ToLowerInvariant();
                    switch (sub)
                    {
                        case "list":
                            if (config.SystemPassPrefixes == null || config.SystemPassPrefixes.Count == 0)
                                capi.ShowChatMessage("[pingme] prefix: (пусто)");
                            else
                                capi.ShowChatMessage("[pingme] prefix: " + string.Join(", ", config.SystemPassPrefixes));
                            break;

                        case "add":
                            if (subparts.Length < 2) { capi.ShowChatMessage("[pingme] .pingme sysprefix add <prefix>"); break; }
                            var pfx = subparts[1].Trim();
                            if (string.IsNullOrEmpty(pfx)) { capi.ShowChatMessage("[pingme] Empty prefix."); break; }
                            if (!config.SystemPassPrefixes.Any(x => x.Equals(pfx, StringComparison.OrdinalIgnoreCase)))
                            {
                                config.SystemPassPrefixes.Add(pfx);
                                capi.StoreModConfig(config, "pingme.json");
                                capi.ShowChatMessage($"[pingme] Add prefix: {pfx}");
                            }
                            else capi.ShowChatMessage($"[pingme] Already exists: {pfx}");
                            break;

                        case "del":
                        case "remove":
                            if (subparts.Length < 2) { capi.ShowChatMessage("[pingme] .pingme sysprefix del <prefix>"); break; }
                            var del = subparts[1].Trim();
                            var removed = false;
                            for (int i = config.SystemPassPrefixes.Count - 1; i >= 0; i--)
                            {
                                if (config.SystemPassPrefixes[i].Equals(del, StringComparison.OrdinalIgnoreCase))
                                {
                                    config.SystemPassPrefixes.RemoveAt(i);
                                    removed = true;
                                }
                            }
                            if (removed)
                            {
                                capi.StoreModConfig(config, "pingme.json");
                                capi.ShowChatMessage($"[pingme] Prefix removed: {del}");
                            }
                            else capi.ShowChatMessage($"[pingme] Not found: {del}");
                            break;

                        case "clear":
                            config.SystemPassPrefixes.Clear();
                            capi.StoreModConfig(config, "pingme.json");
                            capi.ShowChatMessage("[pingme] Prefix list cleared.");
                            break;

                        default:
                            capi.ShowChatMessage("[pingme] Unknown. Use: list | add | del | clear");
                            break;
                    }
                    break;
                }

                default:
                    capi.ShowChatMessage("[pingme] Unknown command. Use: add | show | delete | sound | system | time | sysprefix");
                    break;
            }
        }

        public void OnChatMessage(int groupId, string message, EnumChatType chattype, string data)
        {
            try
            {
                if (string.IsNullOrEmpty(message)) return;

                string clean = StripTags(message);
                clean = OneLine(clean);
                clean = Trunc100(clean);

                if (chattype == EnumChatType.Notification)
                {
                    bool passByPrefix = StartsWithAny(clean, config.SystemPassPrefixes);
                    if (!config.SystemNotifications && !passByPrefix) return;

                    var title = GroupTitle(groupId);
                    toasts.Enqueue(title, clean);
                    return;
                }

                if (!ContainsAnyNicknameWordBounded(clean)) return;

                ParseAuthorAndBody(clean, out string author, out string body);

                var myName = capi?.World?.Player?.PlayerName;
                if (!string.IsNullOrEmpty(author) && !string.IsNullOrEmpty(myName) &&
                    author.Equals(myName, StringComparison.OrdinalIgnoreCase)) return;

                if (!string.IsNullOrEmpty(author)) author += ":";

                var chatTitle = GroupTitle(groupId);
                toasts.EnqueueChat(chatTitle, author, Trunc100(OneLine(body)));
            }
            catch (Exception ex)
            {
                try { capi?.Logger?.Error($"[PingMe] OnChatMessage error: {ex}"); } catch { }
            }
        }

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

        static bool StartsWithAny(string s, List<string> prefixes)
        {
            if (string.IsNullOrEmpty(s) || prefixes == null || prefixes.Count == 0) return false;
            foreach (var p in prefixes)
            {
                if (!string.IsNullOrEmpty(p) && s.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
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

        string GroupTitle(int groupId)
        {
            try
            {
                var player = capi?.World?.Player;
                if (player != null)
                {
                    var membership = player.GetGroup(groupId);
                    if (membership != null && !string.IsNullOrWhiteSpace(membership.GroupName))
                    {
                        return membership.GroupName;
                    }
                }
            }
            catch (Exception ex)
            {
                capi.Logger.Error($"[PingMe] Error while getting group title {groupId}: {ex}");
            }

            if (groupId == 0) return "General chat";
            return $"Group {groupId}";
        }
    }
}
