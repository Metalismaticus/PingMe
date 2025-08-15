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
        public bool SystemNotifications { get; set; } = true;   // show system chats
        public float ToastStaySeconds { get; set; } = 3.8f;     // toast duration
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
                capi.ShowChatMessage("[pingme] Commands: .pingme add <nick>, .pingme show, .pingme delete <nick>, .pingme sound, .pingme system, .pingme time <seconds>");
                return;
            }

            var cmd = parts[1].ToLowerInvariant();
            switch (cmd)
            {
                case "add":
                    if (parts.Length < 3) { capi.ShowChatMessage("[pingme] Provide a nick: .pingme add <nick>"); return; }
                    var toAdd = parts[2].Trim();
                    if (toAdd.Length == 0) { capi.ShowChatMessage("[pingme] Nick is empty."); return; }
                    if (config.Nicknames.Add(toAdd))
                    {
                        capi.StoreModConfig(config, "pingme.json");
                        capi.ShowChatMessage($"[pingme] Added: {toAdd}");
                    }
                    else capi.ShowChatMessage($"[pingme] Already added: {toAdd}");
                    break;

                case "show":
                    capi.ShowChatMessage(
                        config.Nicknames.Count == 0
                        ? "[pingme] List is empty."
                        : "[pingme] " + string.Join(", ", config.Nicknames.OrderBy(s => s))
                    );
                    break;

                case "delete":
                case "del":
                case "remove":
                    if (parts.Length < 3) { capi.ShowChatMessage("[pingme] Provide a nick: .pingme delete <nick>"); return; }
                    var toDel = parts[2].Trim();
                    if (config.Nicknames.Remove(toDel))
                    {
                        capi.StoreModConfig(config, "pingme.json");
                        capi.ShowChatMessage($"[pingme] Deleted: {toDel}");
                    }
                    else capi.ShowChatMessage($"[pingme] Not found: {toDel}");
                    break;

                case "sound":
                    config.SoundEnabled = !config.SoundEnabled;
                    capi.StoreModConfig(config, "pingme.json");
                    capi.ShowChatMessage($"[pingme] Sound: {(config.SoundEnabled ? "on" : "off")}");
                    break;

                case "system":
                    config.SystemNotifications = !config.SystemNotifications;
                    capi.StoreModConfig(config, "pingme.json");
                    capi.ShowChatMessage($"[pingme] System notifications: {(config.SystemNotifications ? "on (all)" : "off")}");
                    break;

                case "time":
                    if (parts.Length < 3)
                    {
                        capi.ShowChatMessage($"[pingme] Now: {config.ToastStaySeconds:0.##} sec. Example: .pingme time 5");
                        return;
                    }
                    var raw = parts[2].Trim().Replace(',', '.');
                    if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float secs))
                    {
                        secs = Math.Max(0.5f, Math.Min(30f, secs));
                        config.ToastStaySeconds = secs;
                        capi.StoreModConfig(config, "pingme.json");
                        capi.ShowChatMessage($"[pingme] Show time: {secs:0.##} sec");
                    }
                    else
                    {
                        capi.ShowChatMessage("[pingme] I didn't understand the number. Example: .pingme time 4.5");
                    }
                    break;

                default:
                    capi.ShowChatMessage("[pingme] Unknown command. Use: add | show | delete | sound | system | time");
                    break;
            }
        }

        public void OnChatMessage(int groupId, string message, EnumChatType chattype, string data)
        {
            if (string.IsNullOrEmpty(message)) return;

            // 1) Strip tags
            string clean = StripTags(message);

            // 2) Collapse line breaks/tabs and extra spaces
            clean = OneLine(clean);

            // 3) Limit length
            clean = Trunc100(clean);

            // System chat
            if (chattype == EnumChatType.Notification)
            {
                if (!config.SystemNotifications) return; // completely disabled; don't filter deaths anymore
                var title = GroupTitle(groupId);
                toasts.Enqueue(title, clean);
                return;
            }

            // Regular chat: nickname filter
            if (!ContainsAnyNicknameWordBounded(clean)) return;

            ParseAuthorAndBody(clean, out string author, out string body);

            // Ignore own messages
            var myName = capi?.World?.Player?.PlayerName;
            if (!string.IsNullOrEmpty(author) && !string.IsNullOrEmpty(myName) &&
                author.Equals(myName, StringComparison.OrdinalIgnoreCase)) return;

            if (!string.IsNullOrEmpty(author)) author += ":";

            var chatTitle = GroupTitle(groupId);
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
