using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace SysMonitorBar;

/// <summary>悬浮条的排版与绘制，OverlayForm 与设置界面预览共用。</summary>
public static class BarPainter
{
    // 支持逐像素 alpha 的排版格式：GenericTypographic 去掉 GDI+ 默认的额外内边距，
    // 让 DrawString 的落点与 MeasureString 的宽度自洽。
    private static readonly StringFormat Typo = new(StringFormat.GenericTypographic)
    {
        FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip | StringFormatFlags.MeasureTrailingSpaces,
        Trimming = StringTrimming.None,
    };

    // 缓存一块 1x1 的 PArgb 位图，用来把任意 alpha 的文字先画进去再合成，
    // 避免 GDI+ 在低 alpha 下直接 DrawString 时出现的描边偏差。
    private static readonly Dictionary<(int, int), SolidBrush> BrushCache = new();

    private static SolidBrush Brush(Color c)
    {
        var key = (c.ToArgb(), 0);
        if (!BrushCache.TryGetValue(key, out var b))
        {
            if (BrushCache.Count > 256) { foreach (var v in BrushCache.Values) v.Dispose(); BrushCache.Clear(); }
            b = new SolidBrush(c);
            BrushCache[key] = b;
        }
        else if (b.Color.ToArgb() != c.ToArgb())
        {
            b.Color = c;
        }
        return b;
    }

    public static List<List<MetricCatalog.Run>> BuildLines(AppConfig cfg, Snapshot snap)
    {
        int maxLine = 0;
        foreach (var it in cfg.Items)
            if (it.Enabled && it.Line > maxLine) maxLine = it.Line;
        if (maxLine > 1) maxLine = 1;   // 最多两行

        var lines = new List<List<MetricCatalog.Run>>();
        for (int li = 0; li <= maxLine; li++)
        {
            var runs = new List<MetricCatalog.Run>();
            bool first = true;
            foreach (var it in cfg.Items)
            {
                if (!it.Enabled || it.Line != li) continue;
                var r = snap?.Get(it.Key) ?? new Reading { Key = it.Key, Label = it.Key, Available = false };
                if (!r.Available && cfg.HideUnavailable) continue;

                var rd = new Reading
                {
                    Key = r.Key,
                    Label = string.IsNullOrWhiteSpace(it.Label) ? r.Label : it.Label,
                    Value = r.Available ? r.Value : "--",
                    Unit = r.Unit,
                    Available = r.Available,
                };

                if (!first && !string.IsNullOrEmpty(cfg.Separator))
                    runs.Add(new MetricCatalog.Run { Text = cfg.Separator, Kind = MetricCatalog.RunKind.Sep });

                runs.AddRange(MetricCatalog.RenderRuns(it.Template, rd));
                first = false;
            }
            lines.Add(runs);
        }

        while (lines.Count > 1 && lines[lines.Count - 1].Count == 0)
            lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    public static int MeasureRunWidth(Graphics g, Font font, string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        // DrawString 在 GenericTypographic 下不会裁掉尾随空格，用 MeasureCharacterRanges
        // 拿到的宽度比 MeasureString 更贴近实际落笔宽度。
        var size = g.MeasureString(text, font, new PointF(0, 0), Typo);
        return (int)Math.Ceiling(size.Width) + 1;
    }

    public static int MeasureLineWidth(Graphics g, Font font, List<MetricCatalog.Run> runs)
    {
        int w = 0;
        foreach (var r in runs) w += MeasureRunWidth(g, font, r.Text);
        return w;
    }

    /// <summary>返回包含内边距的整体尺寸。</summary>
    public static Size Measure(Graphics g, Font font, AppConfig cfg, List<List<MetricCatalog.Run>> lines)
    {
        int textW = 0;
        foreach (var ln in lines) textW = Math.Max(textW, MeasureLineWidth(g, font, ln));
        int n = Math.Max(1, lines.Count);
        int textH = font.Height * n + cfg.LineSpacing * (n - 1);
        return new Size(textW + cfg.PaddingX * 2, textH + cfg.PaddingY * 2);
    }

    public static GraphicsPath RoundedPath(Rectangle b, int r)
    {
        if (r <= 0) r = 0;
        int d = r * 2;
        var p = new GraphicsPath();
        if (r == 0)
        {
            p.AddRectangle(b);
            return p;
        }
        p.AddArc(b.Left, b.Top, d, d, 180, 90);
        p.AddArc(b.Right - d - 1, b.Top, d, d, 270, 90);
        p.AddArc(b.Right - d - 1, b.Bottom - d - 1, d, d, 0, 90);
        p.AddArc(b.Left, b.Bottom - d - 1, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>按 globalAlpha(0~1) 缩放颜色的 alpha 通道。</summary>
    private static Color Scale(Color c, double globalAlpha)
    {
        int a = (int)Math.Round(c.A * globalAlpha);
        if (a < 0) a = 0;
        if (a > 255) a = 255;
        return Color.FromArgb(a, c.R, c.G, c.B);
    }

    /// <summary>
    /// 绘制悬浮条内容。
    /// <paramref name="globalAlpha"/> 为整体不透明度(0~1)，会同时作用于背景与文字；
    /// 背景自身还能通过 <see cref="AppConfig.BackColor"/> 的 alpha 通道做到完全透明。
    /// </summary>
    public static void Paint(Graphics g, Font font, AppConfig cfg, List<List<MetricCatalog.Run>> lines,
        Rectangle bounds, double globalAlpha = 1.0)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        // 背景可能是透明的，此时 ClearType 次像素抗锯齿会出彩边，只能用灰度抗锯齿。
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        var back = Scale(cfg.Back, globalAlpha);
        if (back.A > 0 && bounds.Width > 0 && bounds.Height > 0)
        {
            int r = Math.Max(0, Math.Min(cfg.CornerRadius, Math.Min(bounds.Width, bounds.Height) / 2));
            using var brush = new SolidBrush(back);
            if (r > 0)
            {
                using var path = RoundedPath(new Rectangle(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1), r);
                g.FillPath(brush, path);
            }
            else g.FillRectangle(brush, bounds);
        }

        var textColor = Scale(cfg.Text, globalAlpha);
        var accentColor = Scale(cfg.Accent, globalAlpha);
        var sepColor = Scale(Color.FromArgb(120, cfg.Text.R, cfg.Text.G, cfg.Text.B), globalAlpha);

        if (textColor.A == 0 && accentColor.A == 0) return;

        int y = bounds.Y + cfg.PaddingY;
        foreach (var line in lines)
        {
            int lineW = MeasureLineWidth(g, font, line);
            int x = bounds.X + (bounds.Width - lineW) / 2;
            if (x < bounds.X + cfg.PaddingX) x = bounds.X + cfg.PaddingX;

            foreach (var run in line)
            {
                if (run.Text.Length == 0) continue;
                var color = run.Kind == MetricCatalog.RunKind.Label ? accentColor
                          : run.Kind == MetricCatalog.RunKind.Sep ? sepColor
                          : textColor;
                if (color.A > 0)
                    g.DrawString(run.Text, font, Brush(color), x, y, Typo);
                x += MeasureRunWidth(g, font, run.Text);
            }
            y += font.Height + cfg.LineSpacing;
        }
    }

    public static Font CreateFont(AppConfig cfg)
    {
        var style = cfg.Bold ? FontStyle.Bold : FontStyle.Regular;
        try { return new Font(cfg.FontFamily, cfg.FontSize, style, GraphicsUnit.Point); }
        catch { return new Font("Microsoft YaHei UI", cfg.FontSize, style, GraphicsUnit.Point); }
    }
}
