﻿using Vintagestory.API.Client;
using System;
using System.Collections.Generic;

namespace PingMe
{
    public class ToastManager
    {
        readonly ICoreClientAPI capi;
        readonly Queue<(string title, string desc, string author, bool isChat)> queue = new();
        readonly List<ToastHud> active = new();
        const int MaxActive = 3;

        public const float GapY = 10f;
        public const float PanelH = 96f;
        public const float MarginY = 20f;

        public ToastManager(ICoreClientAPI capi) => this.capi = capi;

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
                var hud = new ToastHud(capi, title, desc, myY, author, isChat);
                hud.ClosedCallback = () =>
                {
                    active.Remove(hud);
                    Reflow();
                    TrySpawn();
                };

                active.Insert(0, hud);
                hud.TryOpen();
            }
        }
    }

    public class ToastHud : HudElement
    {
        const float EnterMs = 250f, StayMs = 3800f, ExitMs = 300f;

        const float MarginX = 20f;
        const float PanelW = 420f;
        const float PanelH = ToastManager.PanelH;

        double t0;
        ElementBounds root;

        float baseY;
        float startY;
        public Action ClosedCallback;

        public ToastHud(ICoreClientAPI capi, string title, string desc, float baseY, string author, bool isChat) : base(capi)
        {
            this.baseY = baseY;
            this.startY = baseY - 10f;

            string uid = Guid.NewGuid().ToString("N");

            root = ElementBounds.Fixed(0, 0, PanelW, PanelH)
                .WithAlignment(EnumDialogArea.LeftTop)
                .WithFixedAlignmentOffset(MarginX, startY);

            var titleFont  = CairoFont.WhiteSmallText(); titleFont.UnscaledFontsize  = 22;
            var descFont   = CairoFont.WhiteSmallText(); descFont.UnscaledFontsize   = 18;
            var authorFont = CairoFont.WhiteSmallText(); authorFont.UnscaledFontsize = 18;
            authorFont.Color = new double[] { 1.0, 0.25, 0.25, 1.0 }; // красный

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

        // Строго максимум 2 строки по пиксельной ширине (без автопереноса на третью).
        static string TwoLines(CairoFont font, string text, float maxWidth)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            // Убираем все переводы строк и схлопываем пробелы
            text = text.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");
            while (text.Contains("  ")) text = text.Replace("  ", " ");

            string line1 = "", line2 = "";
            float w1 = 0, w2 = 0;

            var words = text.Split(' ');
            int i = 0;

            // локальная функция: добавить кусок в линию, если не влазит — вернуть false
            bool TryAppend(ref string line, ref float w, string chunk)
            {
                string candidate = string.IsNullOrEmpty(line) ? chunk : (line + " " + chunk);
                double width = font.GetTextExtents(candidate).Width;
                if (width <= maxWidth) { line = candidate; w = (float)width; return true; }
                return false;
            }

            // локальная функция: разрубить слишком длинное слово под maxWidth (с учётом уже имеющегося line)
            string FitChunk(CairoFont f, string prefix, string word, float maxW)
            {
                // если слово полностью влазит — возвращаем его
                string test = string.IsNullOrEmpty(prefix) ? word : (prefix + " " + word);
                if (f.GetTextExtents(test).Width <= maxW) return word;

                // режем по символам
                string acc = "";
                for (int k = 0; k < word.Length; k++)
                {
                    string cand = string.IsNullOrEmpty(prefix) ? (acc + word[k]) : (prefix + " " + acc + word[k]);
                    if (f.GetTextExtents(cand).Width <= maxW) acc += word[k];
                    else break;
                }
                return acc; // может быть пустым, тогда наверх добавим «…»
            }

            // Сборка первой строки
            for (; i < words.Length; i++)
            {
                string w = words[i];
                if (string.IsNullOrEmpty(w)) continue;

                if (!TryAppend(ref line1, ref w1, w))
                {
                    // слово не лезет — попробуем часть слова
                    string part = FitChunk(font, line1, w, maxWidth);
                    if (!string.IsNullOrEmpty(part))
                    {
                        TryAppend(ref line1, ref w1, part);
                        // остаток слова идёт дальше как следующий элемент
                        string rest = w.Substring(part.Length);
                        if (!string.IsNullOrEmpty(rest)) words[i] = rest; else continue;
                    }
                    break;
                }
            }

            // Сборка второй строки
            for (; i < words.Length; i++)
            {
                string w = words[i];
                if (string.IsNullOrEmpty(w)) continue;

                if (!TryAppend(ref line2, ref w2, w))
                {
                    string part = FitChunk(font, line2, w, maxWidth);
                    if (!string.IsNullOrEmpty(part))
                    {
                        TryAppend(ref line2, ref w2, part);
                    }
                    // есть ещё текст — ставим многоточие (гарантируем влезание)
                    string ell = line2 + "…";
                    while (ell.Length > 1 && font.GetTextExtents(ell).Width > maxWidth)
                    {
                        line2 = line2.Substring(0, line2.Length - 1);
                        ell = line2 + "…";
                    }
                    line2 = ell;
                    return string.IsNullOrEmpty(line2) ? (string.IsNullOrEmpty(line1) ? "" : line1) : (line1 + "\n" + line2);
                }
            }

            // Влезло в две строки без обрезки
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

            if (t <= EnterMs + StayMs)
            {
                root.fixedY = baseY;
                root.CalcWorldBounds();
                return;
            }

            if (t <= EnterMs + StayMs + ExitMs)
            {
                float k = (float)((t - EnterMs - StayMs) / ExitMs);
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
