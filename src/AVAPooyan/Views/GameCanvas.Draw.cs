using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAPooyan.Engine;
using Effect = AVAPooyan.Engine.Effect;

namespace AVAPooyan.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAPooyan/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Fur = 0xFF8A7A6A, PigPink = 0xFFF6A8B8;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused or GameState.GameOver;

    private static readonly uint[] Balloons = [0xFFE8382A, 0xFFF0C020, 0xFF3A8AE8, 0xFF3AB04A];

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF101418), new Rect(0, 0, Field.Width, Field.Height));
        if (InGame || g.State == GameState.Title)
        {
            DrawValley(dc);
            if (InGame)
            {
                foreach (var w in g.Wolves) DrawWolf(dc, w);
                foreach (var r in g.Rocks) DrawRock(dc, r.X, r.Y, 1);
                foreach (var a in g.Arrows) DrawArrow(dc, a.X, a.Y);
                if (g.Thrown is { } m) DrawMeat(dc, m.X, m.Y, _clock * 8, 1);
                DrawPig(dc, g.Player.Y, g.Player.Draw > 0, g.Player.Alive, g.MeatReady, g.Phase == Phase.Dying ? g.PhaseTime : 0);
                if (g.Boulder is { } b) DrawBoulder(dc, b.X, b.Y, b.Spin);
                foreach (var e in g.Effects) DrawEffect(dc, e);
            }
        }
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 14), 24, Yellow, center: true);
    }

    // ───────────────────────────── the valley ─────────────────────────────

    /// <summary>
    /// Sky and far hills; the wolves' clifftop across the top left; the ground; the cliff face with its ledges and rope
    /// ladder; the shaft with the lift's rope; the pulley at the top with two piglets turning it; a tree at the right.
    /// </summary>
    private void DrawValley(DrawingContext dc)
    {
        double top = Field.Hud;
        uint[] sky = [0xFF5AA8E8, 0xFF6AB4EC, 0xFF7ABEEE, 0xFF8CC8F0, 0xFF9ED2F2, 0xFFB0DCF4];
        double band = (Field.Ground - top) / sky.Length;
        for (int i = 0; i < sky.Length; i++) dc.FillRectangle(Brush(sky[i]), new Rect(0, top + i * band, Field.Width, band + 1));
        for (int i = 0; i < 5; i++)
        {
            double cx = (i * 230 + _clock * 8) % 1200 - 100, cy = 200 + (i % 3) * 90;
            for (int k = 0; k < 4; k++) dc.DrawEllipse(Brush(0xD0FFFFFF), null, new Point(cx + k * 22, cy + Math.Sin(k * 2.1) * 6), 26, 16);
        }
        // Far hills.
        var hills = new List<Point> { new(0, Field.Ground) };
        for (int x = 0; x <= 760; x += 40) hills.Add(new Point(x, Field.Ground - 90 - Math.Sin(x * 0.012) * 40 - Math.Sin(x * 0.031) * 18));
        hills.Add(new Point(760, Field.Ground));
        Poly(dc, 0xFF7AB06A, null, 0, [.. hills]);
        // The ground.
        dc.FillRectangle(Brush(0xFF4A8A3A), new Rect(0, Field.Ground, Field.Width, Field.Height - Field.Ground));
        dc.FillRectangle(Brush(0xFF6AAA4A), new Rect(0, Field.Ground, Field.Width, 5));
        for (int i = 0; i < 60; i++)
        {
            uint q = Hash(i, 2, 2);
            dc.DrawLine(Pen(0xFF3A7A2E, 2), new Point(q % 1024, Field.Ground + 8 + (q >> 10) % 50), new Point(q % 1024 + 3, Field.Ground + 2 + (q >> 10) % 50));
        }
        // The wolves' clifftop.
        Poly(dc, 0xFF8A6A4A, 0xFF4A3A2A, 2, new Point(0, top), new Point(Field.ClifftopRight + 30, top), new Point(Field.ClifftopRight + 20, Field.ClifftopY + 6),
            new Point(Field.ClifftopRight - 10, Field.ClifftopY + 22), new Point(0, Field.ClifftopY + 22));
        dc.FillRectangle(Brush(0xFF5A9A3A), new Rect(0, Field.ClifftopY - 4, Field.ClifftopRight + 16, 8), 3);
        for (int i = 0; i < 14; i++)
        {
            uint q = Hash(i, 5, 1);
            dc.DrawEllipse(Brush(0xFF6A5038), null, new Point(20 + q % 560, top + 14 + (q >> 9) % 30), 6 + q % 6, 4);
        }
        // The cliff face, its rope ladder and its ledges.
        double fx = Field.FaceX;
        Poly(dc, 0xFF8A6A4A, 0xFF4A3A2A, 2, new Point(fx - 30, Field.Ground), new Point(fx - 22, 300), new Point(fx - 30, 150), new Point(fx - 10, top),
            new Point(fx + 40, top), new Point(fx + 44, 260), new Point(fx + 38, 480), new Point(fx + 46, Field.Ground));
        for (int i = 0; i < 18; i++)
        {
            uint q = Hash(i, 7, 3);
            dc.DrawEllipse(Brush(0xFF6A5038), null, new Point(fx - 14 + q % 50, top + 30 + (q >> 9) % 600), 5 + q % 5, 3);
        }
        dc.DrawLine(Pen(0xFFB08A50, 2), new Point(fx - 26, Field.Ground), new Point(fx - 26, Field.Ledges[0] - 30));
        dc.DrawLine(Pen(0xFFB08A50, 2), new Point(fx - 14, Field.Ground), new Point(fx - 14, Field.Ledges[0] - 30));
        for (double y = Field.Ground - 10; y > Field.Ledges[0] - 30; y -= 18) dc.DrawLine(Pen(0xFFB08A50, 2), new Point(fx - 26, y), new Point(fx - 14, y));
        foreach (double ly in Field.Ledges)
        {
            dc.FillRectangle(Brush(0xFF6A4A2A), new Rect(fx + 30, ly - 2, 52, 9), 2);
            dc.FillRectangle(Brush(0xFF9A7040), new Rect(fx + 30, ly - 2, 52, 4), 2);
        }
        // The tree at the right, and the shaft between.
        dc.FillRectangle(Brush(0xFF6A4A2A), new Rect(915, top, 70, Field.Ground - top));
        for (int i = 0; i < 12; i++) dc.DrawLine(Pen(0xFF5A3A1E, 2), new Point(925 + i % 4 * 15, top + i * 52), new Point(930 + i % 4 * 15, top + i * 52 + 30));
        dc.DrawEllipse(Brush(0xFF2E7A2E), null, new Point(960, top + 20), 90, 50);
        dc.DrawEllipse(Brush(0xFF3A8A3A), null, new Point(990, top + 4), 60, 36);
        // The pulley and the piglets working it.
        double px = Field.ShaftX, py = top + 30;
        dc.FillRectangle(Brush(0xFF6A4A2A), new Rect(px - 40, py - 6, 80, 10));
        double turn = _clock * 3;
        dc.DrawEllipse(Brush(0xFF9A7040), Pen(0xFF4A3A2A, 2), new Point(px, py + 12), 14, 14);
        dc.DrawLine(Pen(0xFF4A3A2A, 2), new Point(px - Math.Cos(turn) * 14, py + 12 - Math.Sin(turn) * 14), new Point(px + Math.Cos(turn) * 14, py + 12 + Math.Sin(turn) * 14));
        DrawPiglet(dc, px - 30, py - 12, _clock);
        DrawPiglet(dc, px + 30, py - 12, _clock + 1.5);
        dc.DrawLine(Pen(0xFFB08A50, 2), new Point(px, py + 12), new Point(px, Engine.Player.Y - 26));
    }

    private static void DrawPiglet(DrawingContext dc, double x, double y, double t)
    {
        double bob = Math.Sin(t * 6) * 2;
        dc.DrawEllipse(Brush(PigPink), Pen(0xFFB06A80, 1), new Point(x, y + bob), 10, 8);
        dc.DrawEllipse(Brush(PigPink), Pen(0xFFB06A80, 1), new Point(x, y - 9 + bob), 7, 6);
        dc.DrawEllipse(Brush(0xFFE08098), null, new Point(x, y - 7 + bob), 3, 2);
        dc.DrawEllipse(Brush(0xFF1A1A1A), null, new Point(x - 2.5, y - 11 + bob), 1, 1);
        dc.DrawEllipse(Brush(0xFF1A1A1A), null, new Point(x + 2.5, y - 11 + bob), 1, 1);
    }

    // ───────────────────────────── the pig and the wolves ─────────────────────────────

    /// <summary>The mother pig in her wicker basket, bow held out to the left; meat in her other hand when it's ready.</summary>
    private void DrawPig(DrawingContext dc, double y, bool drawn, bool alive, bool meat, double fall)
    {
        double x = Field.ShaftX;
        if (!alive) y += Math.Min(1, fall) * 20;
        // The pig.
        dc.DrawEllipse(Brush(PigPink), Pen(0xFFB06A80, 1.5), new Point(x, y - 6), 18, 16);
        dc.DrawEllipse(Brush(PigPink), Pen(0xFFB06A80, 1.5), new Point(x - 4, y - 24), 13, 12);
        Poly(dc, PigPink, 0xFFB06A80, 1, new Point(x - 12, y - 32), new Point(x - 8, y - 42), new Point(x - 4, y - 32));
        Poly(dc, PigPink, 0xFFB06A80, 1, new Point(x + 2, y - 32), new Point(x + 6, y - 42), new Point(x + 9, y - 31));
        dc.DrawEllipse(Brush(0xFFE08098), Pen(0xFFB06A80, 1), new Point(x - 15, y - 21), 6, 5);
        dc.DrawEllipse(Brush(0xFF7A3040), null, new Point(x - 17, y - 21), 1.3, 1.6);
        dc.DrawEllipse(Brush(0xFF7A3040), null, new Point(x - 13, y - 21), 1.3, 1.6);
        if (alive)
        {
            dc.DrawEllipse(Brush(0xFF1A1A1A), null, new Point(x - 8, y - 28), 1.8, 2.2);
            dc.DrawEllipse(Brush(0xFF1A1A1A), null, new Point(x - 1, y - 28), 1.8, 2.2);
        }
        else
        {
            foreach (double ex in new[] { -8.0, -1 })
            {
                dc.DrawLine(Pen(0xFF1A1A1A, 1.5), new Point(x + ex - 2, y - 30), new Point(x + ex + 2, y - 26));
                dc.DrawLine(Pen(0xFF1A1A1A, 1.5), new Point(x + ex - 2, y - 26), new Point(x + ex + 2, y - 30));
            }
        }
        // A red headscarf.
        Poly(dc, 0xFFD83A3A, null, 0, new Point(x - 13, y - 32), new Point(x + 7, y - 34), new Point(x + 12, y - 26), new Point(x + 16, y - 22), new Point(x + 8, y - 28));
        // The bow.
        double bx = Field.BowX + 6, by = y + Field.BowDy;
        var geo = new StreamGeometry();
        using (var c = geo.Open())
        {
            c.BeginFigure(new Point(bx + 4, by - 22), false);
            c.QuadraticBezierTo(new Point(bx - 14, by), new Point(bx + 4, by + 22));
        }
        dc.DrawGeometry(null, Pen(0xFF8A5A22, 3), geo);
        double pull = drawn ? 4 : 10;
        dc.DrawLine(Pen(0xFFE0E0E0, 1), new Point(bx + 4, by - 22), new Point(bx + pull, by));
        dc.DrawLine(Pen(0xFFE0E0E0, 1), new Point(bx + pull, by), new Point(bx + 4, by + 22));
        if (!drawn && Engine.Arrows.Count < GameEngine.MaxArrows && !meat) DrawArrow(dc, bx - 6, by);
        if (meat) DrawMeat(dc, bx - 4, by - 2, 0, 0.8);
        // The basket.
        dc.FillRectangle(Brush(0xFFC8A060), new Rect(x - 22, y + 2, 44, 26), 4);
        for (int i = 0; i < 4; i++) dc.DrawLine(Pen(0xFF8A6A30, 1.5), new Point(x - 22, y + 6 + i * 6), new Point(x + 22, y + 6 + i * 6));
        for (int i = 0; i < 6; i++) dc.DrawLine(Pen(0xFF9A7A3A, 1), new Point(x - 18 + i * 7, y + 2), new Point(x - 18 + i * 7, y + 28));
        dc.DrawLine(Pen(0xFFB08A50, 2), new Point(x - 20, y + 2), new Point(x, y - 26));
        dc.DrawLine(Pen(0xFFB08A50, 2), new Point(x + 20, y + 2), new Point(x, y - 26));
    }

    private void DrawWolf(DrawingContext dc, Wolf w)
    {
        switch (w.State)
        {
            case WolfState.Floating:
                DrawBalloon(dc, w.X, w.BalloonY, Balloons[w.Balloon]);
                dc.DrawLine(Pen(0xFF4A4A4A, 1), new Point(w.X, w.BalloonY + Wolf.BalloonRadius), new Point(w.X + 4, w.Y - 12));
                DrawWolfBody(dc, w.X, w.Y, Math.Sin(w.T * 3) * 0.15, true, w.T);
                break;
            case WolfState.Falling:
                DrawWolfBody(dc, w.X, w.Y, w.T * 8, false, w.T);
                break;
            case WolfState.Running:
                DrawWolfBody(dc, w.X, w.Y, 0, false, w.T, running: true);
                break;
            case WolfState.Climbing:
                DrawWolfBody(dc, Field.FaceX - 20, w.Y, 0, true, w.T * 2);
                break;
            default:
                DrawWolfBody(dc, w.X, w.Y, 0, false, w.T, snarl: w.Ledge >= 0);
                break;
        }
    }

    private static void DrawBalloon(DrawingContext dc, double x, double y, uint colour)
    {
        dc.DrawEllipse(Brush(colour), Pen(0xFF1A1A1A, 1), new Point(x, y), Wolf.BalloonRadius, Wolf.BalloonRadius * 1.15);
        dc.DrawEllipse(Brush(0x80FFFFFF), null, new Point(x - 5, y - 7), 4, 6);
        Poly(dc, colour, null, 0, new Point(x - 3, y + Wolf.BalloonRadius * 1.15 + 3), new Point(x + 3, y + Wolf.BalloonRadius * 1.15 + 3), new Point(x, y + Wolf.BalloonRadius * 1.1));
    }

    /// <summary>A wolf: grey-brown, long snout, pointed ears, a bushy tail; arms up when hanging on, legs going when running.</summary>
    private static void DrawWolfBody(DrawingContext dc, double x, double y, double tilt, bool armsUp, double t, bool running = false, bool snarl = false)
    {
        using var turn = dc.PushTransform(Matrix.CreateTranslation(-x, -y) * Matrix.CreateRotation(tilt) * Matrix.CreateTranslation(x, y));
        double sw = running ? Math.Sin(t * 16) * 5 : Math.Sin(t * 4) * 2;
        // Tail, legs, body.
        Poly(dc, Fur, 0xFF4A3A2A, 1, new Point(x - 10, y + 4), new Point(x - 24, y - 2 + sw * 0.5), new Point(x - 20, y + 8), new Point(x - 10, y + 10));
        dc.DrawLine(Pen(0xFF5A4A3A, 4), new Point(x - 4, y + 10), new Point(x - 6 + sw, y + 20));
        dc.DrawLine(Pen(0xFF5A4A3A, 4), new Point(x + 4, y + 10), new Point(x + 6 - sw, y + 20));
        dc.DrawEllipse(Brush(Fur), Pen(0xFF4A3A2A, 1.2), new Point(x, y + 2), 11, 12);
        dc.DrawEllipse(Brush(0xFFC8B8A0), null, new Point(x + 2, y + 4), 6, 7);
        if (armsUp)
        {
            dc.DrawLine(Pen(Fur, 4), new Point(x - 6, y - 4), new Point(x - 2, y - 18));
            dc.DrawLine(Pen(Fur, 4), new Point(x + 6, y - 4), new Point(x + 4, y - 18));
        }
        else
        {
            dc.DrawLine(Pen(Fur, 4), new Point(x - 8, y - 2), new Point(x - 12, y + 8 - sw));
            dc.DrawLine(Pen(Fur, 4), new Point(x + 8, y - 2), new Point(x + 12, y + 8 + sw));
        }
        // The head, facing right towards the pig.
        dc.DrawEllipse(Brush(Fur), Pen(0xFF4A3A2A, 1.2), new Point(x + 2, y - 12), 9, 8);
        Poly(dc, Fur, 0xFF4A3A2A, 1.2, new Point(x + 6, y - 15), new Point(x + 20, y - 11), new Point(x + 18, y - 7), new Point(x + 6, y - 7));
        dc.DrawEllipse(Brush(0xFF1A1A1A), null, new Point(x + 19, y - 10), 2, 1.8);
        Poly(dc, 0xFF5A4A3A, null, 0, new Point(x - 4, y - 16), new Point(x - 2, y - 26), new Point(x + 2, y - 17));
        Poly(dc, 0xFF5A4A3A, null, 0, new Point(x + 2, y - 18), new Point(x + 6, y - 27), new Point(x + 8, y - 17));
        dc.DrawEllipse(Brush(0xFFFFE04A), null, new Point(x + 6, y - 14), 1.8, 1.8);
        if (snarl)
        {
            dc.DrawLine(Pen(0xFFFFFFFF, 1.5), new Point(x + 10, y - 7), new Point(x + 17, y - 7));
            for (int i = 0; i < 3; i++) Poly(dc, 0xFFFFFFFF, null, 0, new Point(x + 10 + i * 2.5, y - 7), new Point(x + 11.2 + i * 2.5, y - 4.5), new Point(x + 12.5 + i * 2.5, y - 7));
        }
    }

    private static void DrawArrow(DrawingContext dc, double x, double y)
    {
        dc.DrawLine(Pen(0xFF8A5A22, 2), new Point(x, y), new Point(x + 28, y));
        Poly(dc, 0xFFB0B0B8, null, 0, new Point(x - 6, y), new Point(x + 1, y - 3), new Point(x + 1, y + 3));
        Poly(dc, 0xFFE8382A, null, 0, new Point(x + 22, y), new Point(x + 30, y - 4), new Point(x + 27, y));
        Poly(dc, 0xFFE8382A, null, 0, new Point(x + 22, y), new Point(x + 30, y + 4), new Point(x + 27, y));
    }

    private static void DrawMeat(DrawingContext dc, double x, double y, double spin, double k)
    {
        using var turn = dc.PushTransform(Matrix.CreateTranslation(-x, -y) * Matrix.CreateRotation(spin) * Matrix.CreateTranslation(x, y));
        dc.DrawLine(Pen(0xFFF2EEE0, 4 * k), new Point(x + 6 * k, y), new Point(x + 18 * k, y));
        dc.DrawEllipse(Brush(0xFFF2EEE0), null, new Point(x + 19 * k, y - 3 * k), 3 * k, 3 * k);
        dc.DrawEllipse(Brush(0xFFF2EEE0), null, new Point(x + 19 * k, y + 3 * k), 3 * k, 3 * k);
        dc.DrawEllipse(Brush(0xFFB04A3A), Pen(0xFF6A2A1A, 1.2), new Point(x - 2 * k, y), 13 * k, 10 * k);
        dc.DrawEllipse(Brush(0xFFD86A5A), null, new Point(x - 5 * k, y - 3 * k), 5 * k, 3 * k);
    }

    private static void DrawRock(DrawingContext dc, double x, double y, double k)
    {
        Poly(dc, 0xFF8A8A8A, 0xFF3A3A3A, 1.2, new Point(x - 8 * k, y - 2 * k), new Point(x - 3 * k, y - 8 * k), new Point(x + 6 * k, y - 6 * k), new Point(x + 8 * k, y + 3 * k), new Point(x + 1 * k, y + 8 * k), new Point(x - 7 * k, y + 5 * k));
        dc.DrawEllipse(Brush(0x60FFFFFF), null, new Point(x - 2 * k, y - 3 * k), 2 * k, 1.5 * k);
    }

    private static void DrawBoulder(DrawingContext dc, double x, double y, double spin)
    {
        dc.DrawEllipse(Brush(0xFF7A7A7A), Pen(0xFF2A2A2A, 2), new Point(x, y), Boulder.Radius, Boulder.Radius);
        for (int i = 0; i < 3; i++)
        {
            double a = spin + i * 2.1;
            dc.DrawLine(Pen(0xFF4A4A4A, 2), new Point(x + Math.Cos(a) * 8, y + Math.Sin(a) * 8), new Point(x + Math.Cos(a) * 24, y + Math.Sin(a) * 24));
        }
        dc.DrawEllipse(Brush(0x50FFFFFF), null, new Point(x - 9, y - 10), 7, 5);
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        switch (e.Kind)
        {
            case EffectKind.Pop:
            {
                uint c = Balloons[e.Colour % Balloons.Length];
                for (int i = 0; i < 8; i++)
                {
                    double a = i * Math.PI / 4, d = 8 + 26 * t;
                    dc.DrawLine(Pen(Alpha(c, 1 - t), 3), new Point(e.X + Math.Cos(a) * d * 0.6, e.Y + Math.Sin(a) * d * 0.6), new Point(e.X + Math.Cos(a) * d, e.Y + Math.Sin(a) * d));
                }
                break;
            }
            case EffectKind.Dust:
                dc.DrawEllipse(Brush(Alpha(0xFFE0D0B0, 1 - t)), null, new Point(e.X, e.Y), 4 + 8 * t, 4 + 8 * t);
                break;
            case EffectKind.Score:
                Text(dc, e.Text, new Point(e.X, e.Y - t * 22), 22, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(e.X, e.Y - t * 28), 28, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar, and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF1A1418), new Rect(0, 0, Field.Width, Field.Hud));
        dc.FillRectangle(Brush(0xFFD83A3A), new Rect(0, Field.Hud - 3, Field.Width, 3));
        Text(dc, "SCORE", new Point(16, 18), 20, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(86, 18), 30, Yellow);
        Text(dc, "HI", new Point(Field.Width - 140, 18), 20, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 18), 28, Dim, right: true);
        if (!InGame) return;
        Text(dc, $"ROUND {g.Wave}", new Point(380, 18), 26, White, center: true);
        int spare = Math.Max(0, g.Lives - (g.Player.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, 6); i++) DrawPiglet(dc, 30 + i * 24, 46, 0);
        Text(dc, $"WOLVES {g.ToCome,2}", new Point(560, 18), 22, 0xFFC8B8A0, center: true);
        if (g.Rising) Text(dc, $"AT THE TOP {g.AtTop}/{GameEngine.TopToRoll}", new Point(560, 42), 18, g.AtTop >= 3 ? Red : Dim, center: true);
        if (g.MeatReady && (int)(_clock * 4) % 2 == 0) Text(dc, "MEAT!", new Point(700, 18), 26, 0xFFFF8A6A, center: true);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = 400, cy = 330;
        var (w, h) = (Math.Max(msg.Length * 26, (g.SubMessage?.Length ?? 0) * 15) + 60, g.SubMessage != null ? 106.0 : 70.0);
        dc.FillRectangle(Brush(0xD01A1418), new Rect(cx - w / 2, cy - 46, w, h), 8);
        dc.DrawRectangle(null, Pen(0xFFD83A3A, 2), new Rect(cx - w / 2, cy - 46, w, h), 8);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 14 ? 42 : 54, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 24, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = 400;
        dc.FillRectangle(Brush(0x80101418), new Rect(30, 150, 700, 520), 16);
        string title = "AVA POOYAN";
        for (int i = 0; i < title.Length; i++)
        {
            double bob = Math.Sin(_clock * 3 + i * 0.5) * 4;
            Text(dc, title[i].ToString(), new Point(cx - title.Length * 26 + 26 + i * 52, 210 + bob), 104, i % 2 == 0 ? PigPink : 0xFFFFF0F4, center: true);
        }
        Text(dc, "AFTER THE 1982 ARCADE CLASSIC", new Point(cx, 274), 26, 0xFFE0E0E0, center: true);
        Text(dc, "THE WOLVES ARE AFTER THE PIGLETS - RIDE THE LIFT", new Point(cx, 316), 22, White, center: true);
        Text(dc, "AND SHOOT THEIR BALLOONS", new Point(cx, 342), 22, White, center: true);
        DrawBalloon(dc, cx - 230, 400, Balloons[0]);
        DrawWolfBody(dc, cx - 230, 446, 0, true, _clock);
        Text(dc, $"BALLOON {GameEngine.PopPoints} (RISING {GameEngine.PopPoints * 2})", new Point(cx - 190, 410), 20, 0xFFE0E0E0);
        DrawMeat(dc, cx - 230, 494, _clock * 2, 1);
        Text(dc, "MEAT: FLIES THROUGH THEM ALL, DOUBLING", new Point(cx - 190, 494), 20, 0xFFE0E0E0);
        DrawRock(dc, cx - 230, 536, 1.4);
        Text(dc, $"ROCKS: DODGE OR SHOOT ({GameEngine.RockPoints})", new Point(cx - 190, 536), 20, 0xFFE0E0E0);
        Text(dc, "WOLVES THAT LAND CLIMB TO THE LEDGES AND BITE", new Point(cx, 578), 20, 0xFFFFB0A0, center: true);
        Text(dc, $"{GameEngine.TopToRoll} AT THE TOP ROLL A BOULDER DOWN ON YOU", new Point(cx, 602), 20, 0xFFFFB0A0, center: true);
        if ((int)(_clock * 2) % 2 == 0)
            Text(dc, "PRESS ENTER OR 1 TO PLAY", new Point(cx, 642), 40, Yellow, center: true);
        Text(dc, "UP/DOWN: THE LIFT    SPACE: SHOOT    P: PAUSE    M: SOUND", new Point(cx, 732), 20, White, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF1A1418), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        Text(dc, "BEST OF THE LITTER", new Point(cx, 140), 64, PigPink, center: true);
        Text(dc, "RANK NAME    SCORE   ROUND", new Point(cx, 204), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 248 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF1A1418), new Rect(0, Field.Hud, Field.Width, Field.Height - Field.Hud));
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}
