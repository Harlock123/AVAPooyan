namespace AVAPooyan.Engine;

/// <summary>
/// The computer player, for the demonstration (and the tests). It picks the balloon that's most pressing (the
/// lowest when the wolves are coming down, the highest when they're going up), works out where it will be when an
/// arrow gets there, takes the lift to that height and lets fly. It keeps the lift away from heights where a wolf
/// on a ledge could bite, and from where a rock is about to cross the shaft.
/// </summary>
public sealed class PigPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    public Controls Decide(GameEngine g)
    {
        var p = g.Player;
        if (g.Phase is not (Phase.Play or Phase.Start) || !p.Alive) { Doing = "waiting"; return default; }
        bool Bad(double y) => Danger(g, y);
        double? want = null;
        double best = double.MaxValue;
        foreach (var w in g.Wolves.Where(w => w.State == WolfState.Floating))
        {
            double t = (Field.BowX - w.X) / Arrow.Speed;
            double by = w.BalloonY + w.Vy * t;
            double y = Math.Clamp(by - Field.BowDy, Field.LiftTop, Field.LiftBottom);
            if (Math.Abs(y - (by - Field.BowDy)) > 6 || Bad(y)) continue;
            // Another wolf's body in the way would turn the arrow.
            if (g.Wolves.Any(o => o != w && o.State == WolfState.Floating && o.X > w.X && Math.Abs(o.Y + o.Vy * t - by) < Wolf.Radius)) continue;
            double urgency = g.Rising ? w.Y : -w.Y;
            // Nearer ones first, other things equal: less to travel.
            double cost = urgency + Math.Abs(y - p.Y) * 0.4;
            if (cost < best) { best = cost; want = y; }
        }
        if (want is not { } target)
        {
            Doing = "waiting for one";
            target = Field.LiftTop + 120;
            if (Bad(target)) target = Enumerable.Range(0, 30).Select(i => Field.LiftTop + i * 16.0).FirstOrDefault(y => !Bad(y), p.Y);
        }
        else Doing = "aiming";
        double diff = target - p.Y;
        int dy = Math.Abs(diff) < 3 ? 0 : Math.Sign(diff);
        // Don't ride into a bite or a rock on the way.
        if (dy != 0 && Bad(p.Y + dy * 12)) { dy = Bad(p.Y) ? -dy : 0; Doing = "dodging"; }
        if (Bad(p.Y) && dy == 0)
        {
            dy = !Bad(p.Y - 30) ? -1 : 1;
            Doing = "dodging";
        }
        bool fire = want != null && Math.Abs(diff) < 8 && p.ReloadIn <= 0;
        return new Controls(dy, fire);
    }

    /// <summary>Whether the pig at a height would be bitten, or a rock is about to cross the shaft there.</summary>
    private static bool Danger(GameEngine g, double y)
    {
        foreach (var w in g.Wolves.Where(w => w.State == WolfState.Ledge && w.Ledge >= 0))
            if (Math.Abs(w.Y + Wolf.Radius - (y + Pig.Radius)) < GameEngine.BiteReach + 14) return true;
        foreach (var r in g.Rocks)
        {
            if (r.Vx <= 0) continue;
            double t = (Field.ShaftX - r.X) / r.Vx;
            if (t < 0 || t > 1.2) continue;
            double ry = r.Y + r.Vy * t + 0.5 * GameEngine.Gravity * t * t;
            if (Math.Abs(ry - y) < Pig.Radius + Rock.Radius + 16) return true;
        }
        return false;
    }
}
