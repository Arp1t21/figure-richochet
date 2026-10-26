using System;

namespace Ricochet.Core;

public struct Vec2
{
    public double X;
    public double Y;

    public Vec2(double x, double y) { X = x; Y = y; }

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, double k) => new(a.X * k, a.Y * k);
    public static Vec2 operator *(double k, Vec2 a) => new(a.X * k, a.Y * k);
    public static Vec2 operator /(Vec2 a, double k) => new(a.X / k, a.Y / k);

    public double Len => Math.Sqrt(X * X + Y * Y);
    public double Len2 => X * X + Y * Y;

    public Vec2 Norm()
    {
        var l = Len;
        return l < 1e-12 ? new Vec2() : new Vec2(X / l, Y / l);
    }

    public static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;
    public static double Dist(Vec2 a, Vec2 b) => (a - b).Len;
    public static Vec2 Perp(Vec2 a) => new(-a.Y, a.X);

    public override string ToString() => $"({X:0.#}, {Y:0.#})";
}
