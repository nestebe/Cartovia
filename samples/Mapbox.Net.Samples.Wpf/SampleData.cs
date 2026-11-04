using System;
using System.Linq;
using Mapbox.Net.GeoJson;

namespace Mapbox.Net.Samples.Wpf;

internal static class SampleData
{
    private static readonly (string Name, double Lng, double Lat, int Population)[] FrenchCities =
    {
        ("Paris", 2.3522, 48.8566, 2_133_000),
        ("Marseille", 5.3698, 43.2965, 873_000),
        ("Lyon", 4.8357, 45.7640, 522_000),
        ("Toulouse", 1.4442, 43.6047, 504_000),
        ("Nice", 7.2620, 43.7102, 348_000),
        ("Nantes", -1.5536, 47.2184, 320_000),
        ("Montpellier", 3.8767, 43.6108, 302_000),
        ("Strasbourg", 7.7521, 48.5734, 291_000),
        ("Bordeaux", -0.5792, 44.8378, 261_000),
        ("Lille", 3.0573, 50.6292, 236_000),
        ("Rennes", -1.6778, 48.1173, 222_000),
        ("Reims", 4.0317, 49.2583, 181_000),
        ("Toulon", 5.9280, 43.1242, 180_000),
        ("Grenoble", 5.7245, 45.1885, 158_000),
        ("Dijon", 5.0415, 47.3220, 159_000),
        ("Clermont-Ferrand", 3.0863, 45.7772, 147_000),
        ("Rodez", 2.5753, 44.3506, 25_000),
    };

    public static FeatureCollection Cities()
        => new(FrenchCities.Select(c => new Feature(new Point(c.Lng, c.Lat), new { name = c.Name, population = c.Population })));

    public static FeatureCollection RandomPoints(int count, LngLatBounds bounds, int seed = 42)
    {
        var rnd = new Random(seed);
        return new FeatureCollection(Enumerable.Range(0, count).Select(i => new Feature(
            new Point(bounds.West + rnd.NextDouble() * (bounds.East - bounds.West), bounds.South + rnd.NextDouble() * (bounds.North - bounds.South)),
            new { index = i, weight = rnd.Next(1, 11) })));
    }
}
