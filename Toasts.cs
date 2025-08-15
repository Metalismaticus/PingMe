using Vintagestory.API.Client;
using Vintagestory.API.Common;
using System;
using System.Collections.Generic;

namespace PingMe
{
    public class ToastManager
    {
        readonly ICoreClientAPI capi;
        readonly PingmeConfig config;
        readonly Queue<(string title, string desc, string author, bool isChat)> queue = new();
        readonly List<ToastHud> active = new();
        const int MaxActive = 3;

        public const float GapY = 10f;
        public const float PanelH = 96f;
        public const float MarginY = 20f;

        double lastSoundAtMs = -999999;

        public ToastManager(ICoreClientAPI capi, PingmeConfig config)
        {
            this.capi = capi;
            this.config = config;
        }

        public void Enqueue(string title, string desc)
        {
            queue.Enqueue((title, desc, null, false));
            TrySpawn();
        }

        public void EnqueueChat(string title, string author, string body)
        {
            queue.Enqueue((title, body ?? "", author, true));
            TrySpawn();
        }

        void Reflow()
        {
            for (int i = 0; i < active.Count; i++)
            {
                float y = MarginY + i * (PanelH + GapY);
                active[i].SetBaseY(y);
            }
        }

        void TrySpawn()
        {
            while (active.Count < MaxActive && queue.Count > 0)
            {
                var (title, desc, author, isChat) = queue.Dequeue();

                for (int i = 0; i < active.Count; i++)
                {
                    float y = MarginY + (i + 1) * (PanelH + GapY);
                    active[i].SetBaseY(y);
                }

                float myY = MarginY;
                float stayMs = (config?.ToastStaySeconds > 0 ? config.ToastStaySeconds : 3.8f) * 1000f;

                var hud = new ToastHud(capi, title, desc, myY, author, isChat, stayMs);
                hud.ClosedCallback = () =>
                {
                    active.Remove(hud);
                    Reflow();
                    TrySpawn();
                };

                active.Insert(0, hud);
                hud.TryOpen();

                if (config?.SoundEnabled == true) TryPlayNotifySound();
            }
        }

        void TryPlayNotifySound()
        {
            double now = capi.InWorldEllapsedMilliseconds;
            if (now - lastSoundAtMs < 150) return; // простая защита от лавины в один тик
            lastSoundAtMs = now;

            bool played =
                PlayOnce(new AssetLocation("pingme", "sounds/ui/bells")) ||
                PlayOnce(new AssetLocation("game",   "sounds/notify2"));

            if (!played)
                capi.Logger.Debug("[PingMe] Звук уведомления не проигран (asset не найден/не готов)");
        }

        bool PlayOnce(AssetLocation loc)
        {
            try
            {
                var s = capi.World.LoadSound(new SoundParams
                {
                    Location = loc,
                    ShouldLoop = false,
                    RelativePosition = true,
                    Volume = 1f
                });
                if (s == null) return false;

                s.PlaybackPosition = 0f;
                s.Start();

                int ms = (int)Math.Max(300, s.SoundLengthSeconds * 1000f + 150f);
                capi.Event.RegisterCallback(dt =>
                {
                    try { if (!s.IsDisposed) { if (s.IsPlaying) s.Stop(); s.Dispose(); } }
                    catch { /* no-op */ }
                }, ms);

                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public class ToastHud : HudElement
    {
        const float EnterMs = 250f, ExitMs = 300f;

        const float MarginX = 20f;
        const float PanelW = 420f;
        const float PanelH = ToastManager.PanelH;

        readonly float stayMs;

        double t0;
        ElementBounds root;

        float baseY;
        float startY;
        public Action ClosedCallback;

        public ToastHud(ICoreClientAPI capi, string title, string desc, float baseY, string author, bool isChat, float stayMs)
            : base(capi)
        {
            this.baseY = baseY;
            this.startY = baseY - 10f;
            this.stayMs = stayMs <= 0 ? 3800f : stayMs;

            string uid = Guid.NewGuid().ToString("N");

            root = ElementBounds.Fixed(0, 0, PanelW, PanelH)
                .WithAlignment(EnumDialogArea.LeftTop)
                .WithFixedAlignmentOffset(MarginX, startY);

            var titleFont  = CairoFont.WhiteSmallText(); titleFont.UnscaledFontsize  = 22;
            var descFont   = CairoFont.WhiteSmallText(); descFont.UnscaledFontsize   = 18;
            var authorFont = CairoFont.WhiteSmallText(); authorFont.UnscaledFontsize = 18;
            authorFont.Color = new double[] { 1.0, 0.25, 0.25, 1.0 };

            var tExt = titleFont.GetTextExtents(title ?? "");
            float tW = (float)tExt.Width;
            float tH = (float)tExt.Height;
            float titleX = (PanelW - tW) / 2f;
            float titleY = 10f;
            var tbTitle = ElementBounds.Fixed(titleX, titleY, tW + 2f, tH + 2f);

            GuiStyle.DialogBGRadius = 8;
            var comp = capi.Gui.CreateCompo("project1-toast-" + uid, root)
                .AddDialogBG(ElementBounds.Fill, false, 0.95f)
                .AddStaticText(title ?? "", titleFont, tbTitle);

            float curY = titleY + tH + 10f;

            if (isChat && !string.IsNullOrEmpty(author))
            {
                var aExt = authorFont.GetTextExtents(author);
                float aW = (float)aExt.Width;
                float aH = (float)aExt.Height;

                var tbAuthor = ElementBounds.Fixed(16f, curY, aW + 2f, aH + 2f);
                comp.AddStaticText(author, authorFont, tbAuthor);

                float bodyX = 16f + aW + 8f;
                float bodyW = PanelW - bodyX - 16f;
                string bodyClamped = TwoLines(descFont, desc ?? "", bodyW);
                var tbBody = ElementBounds.Fixed(bodyX, curY, bodyW, PanelH - curY - 10f);
                if (!string.IsNullOrEmpty(bodyClamped))
                    comp.AddStaticText(bodyClamped, descFont, tbBody);
            }
            else
            {
                float descW = PanelW - 32f;
                string descClamped = string.IsNullOrEmpty(desc) ? null : TwoLines(descFont, desc, descW);
                var tbDesc = ElementBounds.Fixed(16f, curY, descW, PanelH - curY - 10f);
                if (!string.IsNullOrEmpty(descClamped))
                    comp.AddStaticText(descClamped, descFont, tbDesc);
            }

            SingleComposer = comp.Compose();
            t0 = capi.InWorldEllapsedMilliseconds;
        }

        // Жёстко максимум 2 строки по пиксельной ширине, с "…"
        static string TwoLines(CairoFont font, string text, float maxWidth)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            text = text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");
            while (text.Contains("  ")) text = text.Replace("  ", " ");

            string[] words = text.Split(' ');
            string line1 = "", line2 = "";

            foreach (var w in words)
            {
                string candidate = string.IsNullOrEmpty(line1) ? w : (line1 + " " + w);
                if (font.GetTextExtents(candidate).Width <= maxWidth)
                {
                    line1 = candidate;
                }
                else
                {
                    // слово не влезло в первую строку → идёт во вторую
                    candidate = string.IsNullOrEmpty(line2) ? w : (line2 + " " + w);
                    if (font.GetTextExtents(candidate).Width <= maxWidth)
                    {
                        line2 = candidate;
                    }
                    else
                    {
                        // не влезло даже во вторую → обрезаем и ставим …
                        string cut = w;
                        while (cut.Length > 0 && font.GetTextExtents(line2 + " " + cut + "…").Width > maxWidth)
                        {
                            cut = cut.Substring(0, cut.Length - 1);
                        }
                        line2 = (string.IsNullOrEmpty(line2) ? cut : (line2 + " " + cut)) + "…";
                        break;
                    }
                }
            }

            return string.IsNullOrEmpty(line2) ? line1 : (line1 + "\n" + line2);
        }

        float EaseOut(float x) => 1f - (float)Math.Pow(1 - x, 3);

        public override void OnRenderGUI(float dt)
        {
            base.OnRenderGUI(dt);
            double t = capi.InWorldEllapsedMilliseconds - t0;

            if (t <= EnterMs)
            {
                float k = EaseOut((float)(t / EnterMs));
                root.fixedY = startY + (baseY - startY) * k;
                root.CalcWorldBounds();
                return;
            }

            if (t <= EnterMs + stayMs)
            {
                root.fixedY = baseY;
                root.CalcWorldBounds();
                return;
            }

            if (t <= EnterMs + stayMs + ExitMs)
            {
                float k = (float)((t - EnterMs - stayMs) / ExitMs);
                root.fixedX = MarginX - 500f * k;
                root.CalcWorldBounds();
                return;
            }

            ClosedCallback?.Invoke();
            TryClose();
        }

        public void SetBaseY(float newBaseY)
        {
            baseY = newBaseY;
            if (root != null)
            {
                root.fixedY = baseY;
                root.CalcWorldBounds();
            }
        }

        public override bool ShouldReceiveMouseEvents() => false;
    }
}
