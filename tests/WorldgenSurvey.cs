using System;
using Godot;

namespace Minecraft;

/// <summary>Finds reproducible representative terrain locations for GPU inspections.</summary>
public partial class WorldgenSurvey : Node
{
    public override void _Ready()
    {
        int[] seeds = [1337, 777, 24681357, -90817263];
        foreach (int seed in seeds)
        {
            SurveySeed(seed);
        }
        GetTree().Quit();
    }

    private static void SurveySeed(int seed)
    {
        WorldGenV7 generator = new(seed, "normal");
        (int X, int Z, int Height, float Score) river = default;
        (int X, int Z, int Height, float Score) mountain = default;
        (int X, int Z, int Height, float Score) forest = default;
        (int X, int Z, int Height, float Score) transition = default;
        (int X, int Z, int Height, float Score) cave = default;

        for (int z = -768; z <= 768; z += 4)
        {
            for (int x = -768; x <= 768; x += 4)
            {
                WorldGenV7.SurfaceSample sample = generator.SampleSurface(x, z, 40);
                WorldGenV7.ClimateSample climate = generator.ClimateAt(x, z, sample.Height);
                if (climate.RiverStrength > 0.78f && sample.Height is >= 35 and <= 47
                        && climate.Continentalness > -0.28f)
                {
                    int bank = Math.Max(
                            Math.Max(generator.SurfaceHeight(x - 12, z, 40), generator.SurfaceHeight(x + 12, z, 40)),
                            Math.Max(generator.SurfaceHeight(x, z - 12, 40), generator.SurfaceHeight(x, z + 12, 40)));
                    float score = climate.RiverStrength * 12.0f + Math.Min(20, bank - sample.Height);
                    if (score > river.Score)
                    {
                        river = (x, z, sample.Height, score);
                    }
                }

                if (sample.Biome == Biome.Mountain && sample.Height > 58)
                {
                    int rough = Math.Abs(sample.Height - generator.SurfaceHeight(x - 8, z, 40))
                            + Math.Abs(sample.Height - generator.SurfaceHeight(x + 8, z, 40))
                            + Math.Abs(sample.Height - generator.SurfaceHeight(x, z - 8, 40))
                            + Math.Abs(sample.Height - generator.SurfaceHeight(x, z + 8, 40));
                    float score = sample.Height + rough * 0.65f;
                    if (score > mountain.Score)
                    {
                        mountain = (x, z, sample.Height, score);
                    }
                }

                if (sample.Biome is Biome.DenseForest or Biome.Jungle or Biome.Taiga or Biome.Birch)
                {
                    bool gridCenter = ((x + 768) % 64 == 0) && ((z + 768) % 64 == 0);
                    if (gridCenter)
                    {
                        int count = CountTrees(generator, x - 24, z - 24, 48);
                        float score = count + (sample.Biome == Biome.DenseForest ? 3.0f : 0.0f);
                        if (score > forest.Score)
                        {
                            forest = (x, z, sample.Height, score);
                        }
                    }
                }

                WorldGenV7.SurfaceSample east = generator.SampleSurface(x + 4, z, 40);
                WorldGenV7.SurfaceSample south = generator.SampleSurface(x, z + 4, 40);
                if ((sample.Biome != east.Biome || sample.Biome != south.Biome)
                        && Math.Abs(sample.Height - east.Height) <= 5
                        && Math.Abs(sample.Height - south.Height) <= 5)
                {
                    float score = 5.0f - Math.Abs(sample.Height - east.Height)
                            + (sample.Biome != east.Biome ? 1.0f : 0.0f)
                            + (sample.Biome != south.Biome ? 1.0f : 0.0f);
                    if (score > transition.Score)
                    {
                        transition = (x, z, sample.Height, score);
                    }
                }

                if (sample.Height > 54)
                {
                    int exposed = 0;
                    for (int depth = 5; depth <= 12; depth++)
                    {
                        if (generator.IsCave(x, sample.Height - depth, z, sample.Height))
                        {
                            exposed++;
                        }
                    }

                    float score = exposed * 2.0f + Math.Max(0, sample.Height - 70) * 0.15f;
                    if (exposed >= 3 && score > cave.Score)
                    {
                        cave = (x, z, sample.Height, score);
                    }
                }
            }
        }

        GD.Print($"WORLDGEN_SURVEY seed={seed} river={Format(river)} mountain={Format(mountain)} forest={Format(forest)} transition={Format(transition)} cave={Format(cave)}");
    }

    private static int CountTrees(WorldGenV7 generator, int ox, int oz, int size)
    {
        int count = 0;
        for (int z = oz; z < oz + size; z++)
        {
            for (int x = ox; x < ox + size; x++)
            {
                WorldGenV7.SurfaceSample sample = generator.SampleSurface(x, z, 40);
                if (sample.Height <= 41 || sample.Height >= 116)
                {
                    continue;
                }

                int divisor = sample.Biome switch
                {
                    Biome.DenseForest => 38,
                    Biome.Jungle => 34,
                    Biome.Taiga => 58,
                    Biome.Birch => 78,
                    _ => 0,
                };
                if (divisor > 0 && generator.CoordinateHash(x, 0, z, 0x54524537u) % (uint)divisor == 0)
                {
                    count++;
                }
            }
        }
        return count;
    }

    private static string Format((int X, int Z, int Height, float Score) value) =>
            $"({value.X},{value.Z})/h{value.Height}/s{value.Score:F1}";
}
