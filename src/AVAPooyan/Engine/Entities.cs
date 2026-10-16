namespace AVAPooyan.Engine;

/// <summary>The controls: the lift up or down (-1 up the screen, 1 down), and the bow.</summary>
public readonly record struct Controls(int Dy = 0, bool Fire = false);

/// <summary>
/// The valley: the wolves' clifftop along the top on the left, the ground at the bottom, and on the right a cliff
/// with ledges up its face beside the shaft the pig's lift runs in.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768, Hud = 60;
    /// <summary>The wolves' clifftop, and how far across it reaches.</summary>
    public const double ClifftopY = 126, ClifftopRight = 600;
    public const double Ground = 704;
    /// <summary>The cliff face the ledges stand out from, and the lift's shaft.</summary>
    public const double FaceX = 760, ShaftX = 860;
    public const double LiftTop = 170, LiftBottom = 660;
    /// <summary>Where the pig's arrows leave the bow, relative to the lift.</summary>
    public const double BowX = ShaftX - 40, BowDy = -6;
    /// <summary>The ledges up the cliff face, where wolves that reach the ground climb to.</summary>
    public static readonly double[] Ledges = [260, 380, 500, 620];
}

/// <summary>The mother pig in her basket on the lift.</summary>
public sealed class Pig
{
    public double Y = Field.LiftTop, ReloadIn, Draw;
    public bool Alive = true;
    public const double Radius = 22;
}

public enum WolfState { Floating, Falling, Running, Climbing, Ledge, Top }

/// <summary>A wolf: floating on a balloon (down from the clifftop, or up from the ground), then on its feet.</summary>
public sealed class Wolf
{
    public double X, Y, Vx, Vy, T, ThrowIn;
    public WolfState State;
    /// <summary>The ledge it's climbing to or standing on.</summary>
    public int Ledge = -1;
    public int Balloon;
    public bool Gone;
    public const double Radius = 14, BalloonRadius = 16, BalloonDy = -40;
    public double BalloonY => Y + BalloonDy;
}

public sealed class Arrow
{
    public double X, Y;
    public bool Gone;
    public const double Speed = 640;
}

/// <summary>A chunk of meat thrown instead of an arrow: it flies on through every balloon in its way.</summary>
public sealed class Meat
{
    public double X, Y;
    public int Chain;
    public bool Gone;
    public const double Speed = 360, Radius = 18;
}

public sealed class Rock
{
    public double X, Y, Vx, Vy;
    public bool Gone;
    public const double Radius = 8;
}

/// <summary>The boulder five wolves at the top roll down the shaft.</summary>
public sealed class Boulder
{
    public double X, Y, Vx, Vy, Spin;
    public const double Radius = 30;
}

public enum EffectKind { Score, Word, Pop, Dust }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8;
    public string Text = "";
    public uint Colour = 0xFFFFE14A;
    public bool Done => Age >= Life;
}
