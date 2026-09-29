using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SysMonitorBar;

/// <summary>悬浮条的排版与绘制，OverlayForm 与设置界面预览共用。</summary>
public static class BarPainter
{
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
        return TextRenderer.MeasureText(g, text, font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;
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

    public static void Paint(Graphics g, Font font, AppConfig cfg, List<List<MetricCatalog.Run>> lines,
        Rectangle bounds, bool opaqueBackground)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = opaqueBackground ? TextRenderingHint.ClearTypeGridFit : TextRenderingHint.AntiAliasGridFit;

        var back = cfg.Back;
        var backColor = opaqueBackground ? back : Color.FromArgb(255, back.R, back.G, back.B);
        int r = Math.Max(0, Math.Min(cfg.CornerRadius, Math.Min(bounds.Width, bounds.Height) / 2));
        using (var brush = new SolidBrush(backColor))
        {
            if (r > 0)
            {
                using var path = RoundedPath(new Rectangle(bounds.X, bounds.Y, bounds.Width - 1, bounds.Height - 1), r);
                g.FillPath(brush, path);
            }
            else g.FillRectangle(brush, bounds);
        }

        var textColor = cfg.Text;
        var accentColor = cfg.Accent;
        var sepColor = Color.FromArgb(120, textColor.R, textColor.G, textColor.B);

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
                TextRenderer.DrawText(g, run.Text, font, new Point(x, y), color,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
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
