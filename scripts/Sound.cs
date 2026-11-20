using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Godot;

namespace Minecraft;

/// <summary>
/// Procedurally synthesized block sounds (no external assets): dig, place,
/// footstep, splash and a looping wind ambience. Positional sounds play
/// through a small pool of AudioStreamPlayer3D voices.
/// </summary>
public partial class Sound : Node
{
    private const int SampleRate = 22050;
    private const int Voices = 8;

    [Signal] public delegate void MuteChangedEventHandler(bool muted);

    public bool Muted => Settings.Muted;

    /// <summary>Music/Sfx/Ambient buses under Master; created once at boot.</summary>
    public static void EnsureBuses()
    {
        EnsureBus("Music");
        EnsureBus("Sfx");
        EnsureBus("Ambient");
    }

    private static void EnsureBus(string busName)
    {
        if (AudioServer.GetBusIndex(busName) != -1)
        {
            return;
        }

        int idx = AudioServer.BusCount;
        AudioServer.AddBus(idx);
        AudioServer.SetBusName(idx, busName);
        AudioServer.SetBusSend(idx, "Master");
    }

    public static void ApplyVolumes()
    {
        SetBusVolume("Master", Settings.MasterVolume);
        SetBusVolume("Music", Settings.MusicVolume);
        SetBusVolume("Sfx", Settings.SfxVolume);
        SetBusVolume("Ambient", Settings.AmbientVolume);
        AudioServer.SetBusMute(AudioServer.GetBusIndex("Master"), Settings.Muted);
    }

    private static void SetBusVolume(string busName, float linear)
    {
        int idx = AudioServer.GetBusIndex(busName);
        if (idx >= 0)
        {
            AudioServer.SetBusVolumeDb(idx, Mathf.LinearToDb(Mathf.Max(linear, 0.0001f)));
        }
    }

    private readonly Dictionary<string, AudioStreamWav> _streams = new();
    private readonly List<AudioStreamPlayer3D> _voicePlayers = [];
    private int _nextVoice;
    private AudioStreamPlayer _wind = null!;
    private AudioStreamPlayer _rain = null!;
    private AudioStreamPlayer _thunderPlayer = null!;
    private AudioStreamPlayer _crickets = null!;
    private AudioEffectLowPassFilter _muffle = null!;
    private int _masterBus = -1;
    private bool _muffled;

    /// <summary>Set by Main after the world/weather exist (ambience scheduling).</summary>
    public Player? PlayerRef;
    public VoxelWorld? WorldRef;
    public Weather? WeatherRef;

    public void Setup(Player player)
    {
        PlayerRef = player;
        _streams["dig"] = SynthDig();
        _streams["dig_dirt"] = SynthDig();
        _streams["dig_stone"] = SynthDigStone();
        _streams["dig_wood"] = SynthDigWood();
        _streams["dig_sand"] = SynthDigSand();
        _streams["dig_glass"] = SynthDigGlass();
        _streams["dig_leaves"] = SynthDigLeaves();
        _streams["place"] = SynthPlace();
        _streams["place_dirt"] = SynthPlace(170.0f, 0.30f);
        _streams["place_stone"] = SynthPlace(105.0f, 0.18f);
        _streams["place_wood"] = SynthPlace(220.0f, 0.12f);
        _streams["place_sand"] = SynthPlace(145.0f, 0.34f);
        _streams["place_glass"] = SynthPlace(690.0f, 0.22f);
        _streams["place_leaves"] = SynthPlace(410.0f, 0.46f);
        _streams["step"] = SynthStep();
        _streams["splash"] = SynthSplash();
        _streams["baa"] = SynthBaa();
        _streams["hurt"] = SynthHurt();
        _streams["mobhurt"] = SynthMobHurt();
        _streams["mobdie"] = SynthMobDie();
        _streams["mobgrowl"] = SynthMobGrowl();
        _streams["mobchitter"] = SynthMobChitter();
        _streams["mobcrystal"] = SynthMobCrystal();
        _streams["mobdread"] = SynthMobDread();
        _streams["eat"] = SynthEat();
        _streams["sizzle"] = SynthSizzle();
        for (int i = 0; i < Voices; i++)
        {
            AudioStreamPlayer3D v = new()
            {
                MaxDistance = 26.0f,
                UnitSize = 6.0f,
                Bus = "Sfx",
            };
            AddChild(v);
            _voicePlayers.Add(v);
        }

        _wind = new AudioStreamPlayer
        {
            Stream = SynthWind(),
            VolumeDb = -22.0f,
            Bus = "Ambient",
        };
        AddChild(_wind);
        _wind.Play();
        _rain = new AudioStreamPlayer
        {
            Stream = SynthRain(),
            VolumeDb = -60.0f,
            Bus = "Ambient",
        };
        AddChild(_rain);
        _rain.Play();
        _crickets = new AudioStreamPlayer
        {
            Stream = SynthCrickets(),
            VolumeDb = -60.0f,
            Bus = "Ambient",
        };
        AddChild(_crickets);
        _crickets.Play();
        _thunderPlayer = new AudioStreamPlayer
        {
            Stream = SynthThunder(),
            VolumeDb = -3.0f,
            Bus = "Sfx",
        };
        AddChild(_thunderPlayer);

        // Underwater muffle: one low-pass on the Master bus, toggled by state.
        _masterBus = AudioServer.GetBusIndex("Master");
        _muffle = new AudioEffectLowPassFilter
        {
            CutoffHz = 650.0f,
        };
        AudioServer.AddBusEffect(_masterBus, _muffle, 0);
        AudioServer.SetBusEffectEnabled(_masterBus, 0, false);

        player.BlockBroken += OnBroken;
        player.BlockPlaced += OnPlaced;
        player.Stepped += OnStepped;
        player.Splashed += OnSplashed;
        player.MiningTick += OnMiningTick;
        player.Stats.Damaged += _ => Play("hurt", player.GlobalPosition, 1.0f, -2.0f);
        player.Ate += () => Play("eat", player.GlobalPosition, 1.0f, -6.0f);
        player.CampfireUsed += _ => Play("sizzle", player.GlobalPosition, 1.0f, -4.0f);
    }

    /// <summary>
    /// Adaptive ambience: wind rises with clouds, crickets own the night
    /// (unless rain or water swallow them), and everything muffles underwater.
    /// </summary>
    public override void _Process(double delta)
    {
        if (PlayerRef == null)
        {
            return;
        }

        bool under = PlayerRef.Underwater;
        if (under != _muffled)
        {
            _muffled = under;
            AudioServer.SetBusEffectEnabled(_masterBus, 0, _muffled);
        }

        float clouds = WeatherRef == null ? 0.0f : (float)WeatherRef.Cloudiness;
        _wind.VolumeDb = Mathf.Lerp(-23.0f, -15.0f, clouds);

        double nightF = 0.0;
        if (WorldRef != null)
        {
            double t = WorldRef.TimeOfDay;
            nightF = System.Math.Clamp(-System.Math.Sin((t - 0.25) * System.Math.Tau) * 1.8, 0.0, 1.0);
        }

        bool raining = WeatherRef != null && (int)WeatherRef.Current >= 2;
        double crickets = nightF * (raining ? 0.12 : 1.0) * (under ? 0.0 : 1.0);
        _crickets.VolumeDb = Mathf.Lerp(-60.0f, -27.0f, (float)crickets);
    }

    /// <summary>Material family for dig and step sounds.</summary>
    private static string FamilyFor(BlockId id) => id switch
    {
        BlockId.Glass => "dig_glass",
        BlockId.Leaves or BlockId.BirchLeaves or BlockId.TallGrass
                or BlockId.FlowerRed or BlockId.FlowerYellow or BlockId.Cactus
                or BlockId.Wheat0 or BlockId.Wheat1 or BlockId.Wheat2
                or BlockId.Torch => "dig_leaves",
        BlockId.Stone or BlockId.Cobblestone or BlockId.Brick or BlockId.CoalOre
                or BlockId.IronOre or BlockId.GoldOre or BlockId.DiamondOre
                or BlockId.Bedrock or BlockId.Furnace => "dig_stone",
        BlockId.Log or BlockId.BirchLog or BlockId.Planks or BlockId.CraftingTable
                or BlockId.Chest or BlockId.Bed or BlockId.Ladder => "dig_wood",
        BlockId.Sand or BlockId.Gravel or BlockId.Snow => "dig_sand",
        _ => "dig_dirt",
    };

    private static string PlaceFamilyFor(BlockId id) => id switch
    {
        BlockId.Glass => "place_glass",
        BlockId.Leaves or BlockId.BirchLeaves or BlockId.TallGrass
                or BlockId.FlowerRed or BlockId.FlowerYellow or BlockId.Cactus
                or BlockId.Torch => "place_leaves",
        BlockId.Stone or BlockId.Cobblestone or BlockId.Brick or BlockId.CoalOre
                or BlockId.IronOre or BlockId.GoldOre or BlockId.DiamondOre
                or BlockId.Bedrock or BlockId.Furnace => "place_stone",
        BlockId.Log or BlockId.BirchLog or BlockId.Planks or BlockId.CraftingTable
                or BlockId.Chest or BlockId.Bed or BlockId.Ladder => "place_wood",
        BlockId.Sand or BlockId.Gravel or BlockId.Snow => "place_sand",
        _ => "place_dirt",
    };

    private void OnMiningTick(Vector3 pos, int id)
    {
        Play(FamilyFor((BlockId)id), pos, PitchFor((BlockId)id) * 0.9f, -7.0f);
    }

    /// <summary>Weather hook: rain hiss level 0..1 (0 mutes the loop).</summary>
    public void SetRain(float level)
    {
        if (_rain == null)
        {
            return;
        }

        _rain.VolumeDb = Mathf.Lerp(-60.0f, -13.0f, Mathf.Clamp(level, 0.0f, 1.0f));
    }

    /// <summary>Weather hook: schedule a thunder rumble after <paramref name="delay"/> seconds.</summary>
    public void Thunder(float delay)
    {
        if (_thunderPlayer == null)
        {
            return;
        }

        GetTree().CreateTimer(System.Math.Max(delay, 0.05f)).Timeout += () =>
        {
            if (!Muted)
            {
                _thunderPlayer.Play();
            }
        };
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.IsActionPressed("toggle_mute"))
        {
            Settings.Muted = !Settings.Muted;
            Settings.Save();
            ApplyVolumes();
            EmitSignal(SignalName.MuteChanged, Settings.Muted);
        }
    }

    public void Play(string kind, Vector3 pos, float pitch = 1.0f, float volumeDb = 0.0f)
    {
        if (Muted || !_streams.TryGetValue(kind, out AudioStreamWav? stream))
        {
            return;
        }

        AudioStreamPlayer3D v = _voicePlayers[_nextVoice];
        _nextVoice = (_nextVoice + 1) % Voices;
        v.GlobalPosition = pos;
        v.Stream = stream;
        v.PitchScale = pitch * (float)GD.RandRange(0.92, 1.08);
        v.VolumeDb = volumeDb;
        v.Play();
    }

    private static float PitchFor(BlockId id) => id switch
    {
        BlockId.Dirt or BlockId.Grass or BlockId.Sand or BlockId.Gravel
                or BlockId.Snow or BlockId.SnowGrass => 1.25f,
        BlockId.Leaves or BlockId.BirchLeaves or BlockId.TallGrass
                or BlockId.FlowerRed or BlockId.FlowerYellow => 1.45f,
        BlockId.Glass => 1.35f,
        BlockId.Stone or BlockId.Cobblestone or BlockId.Brick or BlockId.CoalOre
                or BlockId.IronOre or BlockId.GoldOre or BlockId.DiamondOre or BlockId.Bedrock => 0.75f,
        _ => 1.0f,
    };

    private void OnBroken(Vector3 pos, int id)
    {
        Play(FamilyFor((BlockId)id), pos, PitchFor((BlockId)id));
    }

    private void OnPlaced(Vector3 pos, int id)
    {
        BlockId blockId = (BlockId)id;
        Play(PlaceFamilyFor(blockId), pos, PitchFor(blockId), -2.0f);
    }

    private void OnStepped(Vector3 pos, int id)
    {
        Play("step", pos, PitchFor((BlockId)id) * 1.1f, -8.0f);
    }

    private void OnSplashed(Vector3 pos)
    {
        Play("splash", pos, 1.0f, -2.0f);
    }

    // -----------------------------------------------------------------------
    // Synthesis (16-bit mono PCM at SampleRate)
    // -----------------------------------------------------------------------

    /// <summary>Packs float frames into 16-bit little-endian PCM via spans —
    /// bounds-checked, endianness-explicit, no manual byte twiddling.</summary>
    private static AudioStreamWav MakeWav(float[] frames)
    {
        byte[] data = new byte[frames.Length * 2];
        Span<byte> span = data;
        for (int i = 0; i < frames.Length; i++)
        {
            short sample = (short)(int)(Mathf.Clamp(frames[i], -1.0f, 1.0f) * 32767.0f);
            BinaryPrimitives.WriteInt16LittleEndian(span[(i * 2)..], sample);
        }

        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = SampleRate,
            Stereo = false,
            Data = data,
        };
    }

    private static AudioStreamWav SynthDig()
    {
        int n = (int)(SampleRate * 0.13);
        float[] outFrames = new float[n];
        RandomNumberGenerator rng = new()
        {
            Seed = 1001,
        };
        double lp = 0.0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double env = System.Math.Pow(1.0 - t, 2.2);
            lp = lp * 0.82 + rng.RandfRange(-1.0f, 1.0f) * 0.18;
            outFrames[i] = (float)(lp * env * 2.4);
        }

        return MakeWav(outFrames);
    }

    private static AudioStreamWav SynthPlace(float baseFrequency = 230.0f, float noiseAmount = 0.15f)
    {
        int n = (int)(SampleRate * 0.1);
        float[] outFrames = new float[n];
        RandomNumberGenerator rng = new()
        {
            Seed = 2002,
        };
        double phase = 0.0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double env = System.Math.Pow(1.0 - t, 3.0);
            double freq = baseFrequency * (1.0 - 0.48 * t);
            phase += System.Math.Tau * freq / SampleRate;
            double tone = System.Math.Sin(phase);
            double noise = rng.RandfRange(-1.0f, 1.0f) * noiseAmount;
            outFrames[i] = (float)((tone * (1.0 - noiseAmount * 0.45) + noise) * env * 0.9);
        }

        return MakeWav(outFrames);
    }

    private static AudioStreamWav SynthDigGlass()
    {
        int n = (int)(SampleRate * 0.16);
        float[] frames = new float[n];
        RandomNumberGenerator rng = new() { Seed = 6106 };
        double phase = 0.0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double env = System.Math.Pow(1.0 - t, 3.5);
            double frequency = 1150.0 - 620.0 * t;
            phase += System.Math.Tau * frequency / SampleRate;
            double click = System.Math.Sin(phase) * 0.52
                    + System.Math.Sin(phase * 2.7) * 0.14;
            frames[i] = (float)((click + rng.RandfRange(-1.0f, 1.0f) * 0.14) * env);
        }

        return MakeWav(frames);
    }

    private static AudioStreamWav SynthDigLeaves()
    {
        int n = (int)(SampleRate * 0.18);
        float[] frames = new float[n];
        RandomNumberGenerator rng = new() { Seed = 6206 };
        double lowPass = 0.0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double env = System.Math.Pow(1.0 - t, 1.8);
            lowPass = lowPass * 0.62 + rng.RandfRange(-1.0f, 1.0f) * 0.38;
            frames[i] = (float)(lowPass * env * 0.9);
        }

        return MakeWav(frames);
    }

    private static AudioStreamWav SynthStep()
    {
        int n = (int)(SampleRate * 0.06);
        float[] outFrames = new float[n];
        RandomNumberGenerator rng = new()
        {
            Seed = 3003,
        };
        double lp = 0.0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double env = System.Math.Pow(1.0 - t, 3.0);
            lp = lp * 0.9 + rng.RandfRange(-1.0f, 1.0f) * 0.1;
            outFrames[i] = (float)(lp * env * 2.0);
        }

        return MakeWav(outFrames);
    }

    private static AudioStreamWav SynthSplash()
    {
        int n = (int)(SampleRate * 0.35);
        float[] outFrames = new float[n];
        RandomNumberGenerator rng = new()
        {
            Seed = 4004,
        };
        double lp = 0.0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double env = System.Math.Sin(System.Math.PI * t);
            lp = lp * 0.88 + rng.RandfRange(-1.0f, 1.0f) * 0.12;
            double bubbles = System.Math.Sin(System.Math.Tau * 9.0 * t) * 0.2;
            outFrames[i] = (float)((lp * 1.6 + bubbles * env) * env);
        }

        return MakeWav(outFrames);
    }

    private static AudioStreamWav SynthBaa()
    {
        int n = (int)(SampleRate * 0.42);
        float[] outFrames = new float[n];
        double phase = 0.0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double env = System.Math.Sin(System.Math.PI * t);
            double freq = (250.0 - 70.0 * t) * (1.0 + 0.06 * System.Math.Sin(System.Math.Tau * 11.0 * t));
            phase += System.Math.Tau * freq / SampleRate;
            double tone = System.Math.Sin(phase) * 0.6 + System.Math.Sin(phase * 2.0) * 0.25;
            outFrames[i] = (float)(tone * env * 0.8);
        }

        return MakeWav(outFrames);
    }

    /// <summary>Player pain: a short voiced "oof".</summary>
    private static AudioStreamWav SynthHurt()
    {
        int n = (int)(SampleRate * 0.26);
        float[] outFrames = new float[n];
        RandomNumberGenerator rng = new()
        {
            Seed = 8008,
        };
        double phase = 0.0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double env = System.Math.Sin(System.Math.PI * t);
            double freq = 265.0 - 140.0 * t;
            phase += System.Math.Tau * freq / SampleRate;
            double tone = System.Math.Sin(phase) * 0.55 + System.Math.Sin(phase * 2.0) * 0.2;
            outFrames[i] = (float)((tone + rng.RandfRange(-1.0f, 1.0f) * 0.18) * env * 0.85);
        }

        return MakeWav(outFrames);
    }

    /// <summary>Mob hurt: lower and gruffer than the player's.</summary>
    private static AudioStreamWav SynthMobHurt()
    {
        int n = (int)(SampleRate * 0.3);
        float[] outFrames = new float[n];
        RandomNumberGenerator rng = new()
        {
            Seed = 8118,
        };
        double phase = 0.0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double env = System.Math.Sin(System.Math.PI * t) * (1.0 - t * 0.4);
            double freq = 165.0 - 70.0 * t;
            phase += System.Math.Tau * freq / SampleRate;
            double tone = System.Math.Sin(phase) * 0.5 + System.Math.Sin(phase * 0.5) * 0.3;
            outFrames[i] = (float)((tone + rng.RandfRange(-1.0f, 1.0f) * 0.22) * env * 0.85);
        }

        return MakeWav(outFrames);
    }

    /// <summary>Mob death: a long descending sigh into the dark.</summary>
    private static AudioStreamWav SynthMobDie()
    {
        int n = (int)(SampleRate * 0.7);
        float[] outFrames = new float[n];
        RandomNumberGenerator rng = new()
        {
            Seed = 8228,
        };
        double phase = 0.0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double env = System.Math.Pow(1.0 - t, 1.4);
            double freq = 150.0 - 95.0 * t;
            phase += System.Math.Tau * freq / SampleRate;
            double tone = System.Math.Sin(phase) * 0.5 + System.Math.Sign(System.Math.Sin(phase * 0.5)) * 0.12;
            outFrames[i] = (float)((tone + rng.RandfRange(-1.0f, 1.0f) * 0.1) * env * 0.8);
        }

        return MakeWav(outFrames);
    }


    /// <summary>Low wooden growl used by the defensive Briarback and newborns.</summary>
    private static AudioStreamWav SynthMobGrowl()
    {
        int n = (int)(SampleRate * 0.38);
        float[] frames = new float[n];
        RandomNumberGenerator rng = new() { Seed = 8611 };
        double phase = 0.0;
        double lowPass = 0.0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double envelope = System.Math.Pow(System.Math.Sin(System.Math.PI * t), 0.8);
            double frequency = 112.0 - 52.0 * t + 8.0 * System.Math.Sin(t * 18.0);
            phase += System.Math.Tau * frequency / SampleRate;
            lowPass = lowPass * 0.84 + rng.RandfRange(-1.0f, 1.0f) * 0.16;
            double tone = System.Math.Sin(phase) * 0.72 + System.Math.Sin(phase * 1.7) * 0.18;
            frames[i] = (float)((tone + lowPass * 0.22) * envelope * 0.72);
        }

        return MakeWav(frames);
    }

    /// <summary>Dry, rapid clicks for the stalking cave predator and Hollow.</summary>
    private static AudioStreamWav SynthMobChitter()
    {
        int n = (int)(SampleRate * 0.24);
        float[] frames = new float[n];
        RandomNumberGenerator rng = new() { Seed = 8622 };
        double phase = 0.0;
        double last = 0.0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double pulse = 0.0;
            for (int click = 0; click < 4; click++)
            {
                double start = click * 0.052;
                double local = t - start;
                if (local >= 0.0 && local < 0.026)
                {
                    pulse += System.Math.Pow(1.0 - local / 0.026, 2.0);
                }
            }

            phase += System.Math.Tau * (1250.0 + 520.0 * t) / SampleRate;
            double noise = rng.RandfRange(-1.0f, 1.0f);
            double highPass = noise - last * 0.72;
            last = noise;
            frames[i] = (float)((System.Math.Sin(phase) * 0.22 + highPass * 0.48) * pulse);
        }

        return MakeWav(frames);
    }

    /// <summary>Bright, glassy shimmer for crystal creatures and their projectiles.</summary>
    private static AudioStreamWav SynthMobCrystal()
    {
        int n = (int)(SampleRate * 0.34);
        float[] frames = new float[n];
        double phaseA = 0.0;
        double phaseB = 0.0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double envelope = System.Math.Sin(System.Math.PI * t) * (1.0 - 0.25 * t);
            double frequency = 720.0 + 420.0 * t;
            phaseA += System.Math.Tau * frequency / SampleRate;
            phaseB += System.Math.Tau * frequency * 1.503 / SampleRate;
            double tone = System.Math.Sin(phaseA) * 0.55 + System.Math.Sin(phaseB) * 0.22;
            frames[i] = (float)(tone * envelope * 0.52);
        }

        return MakeWav(frames);
    }

    /// <summary>Descending, airy call for the rare Dreadwing.</summary>
    private static AudioStreamWav SynthMobDread()
    {
        int n = (int)(SampleRate * 0.48);
        float[] frames = new float[n];
        RandomNumberGenerator rng = new() { Seed = 8633 };
        double phase = 0.0;
        double lowPass = 0.0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double envelope = System.Math.Pow(1.0 - t, 1.35);
            double frequency = 480.0 - 290.0 * t + 34.0 * System.Math.Sin(t * 15.0);
            phase += System.Math.Tau * frequency / SampleRate;
            lowPass = lowPass * 0.96 + rng.RandfRange(-1.0f, 1.0f) * 0.04;
            frames[i] = (float)((System.Math.Sin(phase) * 0.55
                    + System.Math.Sin(phase * 0.53) * 0.20 + lowPass * 0.18) * envelope * 0.68);
        }

        return MakeWav(frames);
    }

    /// <summary>Eating: three soft crunches.</summary>
    private static AudioStreamWav SynthEat()
    {
        int n = (int)(SampleRate * 0.42);
        float[] outFrames = new float[n];
        RandomNumberGenerator rng = new()
        {
            Seed = 8338,
        };
        double lp = 0.0;
        double[] bites = [0.0, 0.14, 0.28];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / SampleRate;
            double env = 0.0;
            foreach (double b in bites)
            {
                if (t >= b && t < b + 0.09)
                {
                    env += System.Math.Pow(1.0 - (t - b) / 0.09, 1.8);
                }
            }

            lp = lp * 0.72 + rng.RandfRange(-1.0f, 1.0f) * 0.28;
            outFrames[i] = (float)(lp * env * 1.4);
        }

        return MakeWav(outFrames);
    }

    /// <summary>Campfire cooking: a short high sizzle.</summary>
    private static AudioStreamWav SynthSizzle()
    {
        int n = (int)(SampleRate * 0.55);
        float[] outFrames = new float[n];
        RandomNumberGenerator rng = new()
        {
            Seed = 8448,
        };
        double last = 0.0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double env = System.Math.Pow(1.0 - t, 0.9);
            double w = rng.RandfRange(-1.0f, 1.0f);
            double hp = w - last * 0.82;
            last = w;
            double wobble = 0.7 + 0.3 * System.Math.Sin(System.Math.Tau * 13.0 * t);
            outFrames[i] = (float)(hp * env * wobble * 0.5);
        }

        return MakeWav(outFrames);
    }

    private static AudioStreamWav SynthDigStone()
    {
        // Crunchy rock: low-passed noise with a hard attack and a click.
        int n = (int)(SampleRate * 0.11);
        float[] outFrames = new float[n];
        RandomNumberGenerator rng = new()
        {
            Seed = 1111,
        };
        double lp = 0.0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double env = System.Math.Pow(1.0 - t, 2.8);
            lp = lp * 0.66 + rng.RandfRange(-1.0f, 1.0f) * 0.34;
            double click = t < 0.02 ? System.Math.Sin(System.Math.Tau * 950.0 * ((double)i / SampleRate)) * 0.4 : 0.0;
            outFrames[i] = (float)((lp * 1.9 + click) * env);
        }

        return MakeWav(outFrames);
    }

    private static AudioStreamWav SynthDigWood()
    {
        // Hollow knock: a damped low resonance over a soft noise attack.
        int n = (int)(SampleRate * 0.12);
        float[] outFrames = new float[n];
        RandomNumberGenerator rng = new()
        {
            Seed = 1212,
        };
        double phase = 0.0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double env = System.Math.Pow(1.0 - t, 3.2);
            double freq = 210.0 - 60.0 * t;
            phase += System.Math.Tau * freq / SampleRate;
            double tone = System.Math.Sin(phase) * 0.7 + System.Math.Sin(phase * 2.6) * 0.2;
            outFrames[i] = (float)((tone + rng.RandfRange(-1.0f, 1.0f) * 0.12) * env);
        }

        return MakeWav(outFrames);
    }

    private static AudioStreamWav SynthDigSand()
    {
        // Dry hiss: high-passed noise, no body.
        int n = (int)(SampleRate * 0.1);
        float[] outFrames = new float[n];
        RandomNumberGenerator rng = new()
        {
            Seed = 1313,
        };
        double last = 0.0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double env = System.Math.Pow(1.0 - t, 2.0);
            double w = rng.RandfRange(-1.0f, 1.0f);
            double hp = w - last * 0.85;
            last = w;
            outFrames[i] = (float)(hp * env * 1.1);
        }

        return MakeWav(outFrames);
    }

    /// <summary>Night crickets: three-chirp bursts with a wandering pitch.</summary>
    private static AudioStreamWav SynthCrickets()
    {
        const double seconds = 4.0;
        int n = (int)(SampleRate * seconds);
        float[] outFrames = new float[n];
        RandomNumberGenerator rng = new()
        {
            Seed = 9505,
        };
        double phase = 0.0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / SampleRate;
            double env = 0.0;
            double cycle = t % 0.62;
            for (int b = 0; b < 3; b++)
            {
                double bt = cycle - b * 0.055;
                if (bt >= 0.0 && bt < 0.04)
                {
                    env += System.Math.Sin(System.Math.PI * bt / 0.04) * (1.0 - b * 0.22);
                }
            }

            double freq = 4000.0 + 500.0 * System.Math.Sin(t * 1.7);
            phase += System.Math.Tau * freq / SampleRate;
            double tone = System.Math.Sin(phase) * 0.5 + System.Math.Sin(phase * 0.5) * 0.2;
            outFrames[i] = (float)(tone * env * 0.16 + rng.RandfRange(-1.0f, 1.0f) * 0.004 * env);
        }

        return SetLoop(MakeWav(outFrames), n);
    }

    private static AudioStreamWav SynthWind()
    {
        const double seconds = 4.0;
        int n = (int)(SampleRate * seconds);
        float[] outFrames = new float[n];
        RandomNumberGenerator rng = new()
        {
            Seed = 5005,
        };
        double brown = 0.0;
        for (int i = 0; i < n; i++)
        {
            brown = System.Math.Clamp(brown * 0.996 + rng.RandfRange(-1.0f, 1.0f) * 0.02, -1.0, 1.0);
            double swell = 0.6 + 0.4 * System.Math.Sin(System.Math.Tau * i / n);
            outFrames[i] = (float)(brown * 0.7 * swell);
        }

        return SetLoop(MakeWav(outFrames), n);
    }

    private static AudioStreamWav SynthRain()
    {
        const double seconds = 3.0;
        int n = (int)(SampleRate * seconds);
        float[] outFrames = new float[n];
        RandomNumberGenerator rng = new()
        {
            Seed = 6006,
        };
        double last = 0.0;
        for (int i = 0; i < n; i++)
        {
            double w = rng.RandfRange(-1.0f, 1.0f);
            double hp = w - last * 0.7;
            last = w;
            double gust = 0.75 + 0.25 * System.Math.Sin(System.Math.Tau * 2.0 * i / n);
            outFrames[i] = (float)(hp * 0.35 * gust);
        }

        return SetLoop(MakeWav(outFrames), n);
    }

    private static AudioStreamWav SetLoop(AudioStreamWav wav, int loopEnd)
    {
        wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
        wav.LoopBegin = 0;
        wav.LoopEnd = loopEnd;
        return wav;
    }

    private static AudioStreamWav SynthThunder()
    {
        const double seconds = 3.4;
        int n = (int)(SampleRate * seconds);
        float[] outFrames = new float[n];
        RandomNumberGenerator rng = new()
        {
            Seed = 7007,
        };
        double brown = 0.0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double env = System.Math.Pow(1.0 - t, 1.5)
                    * (0.4 + 0.6 * System.Math.Sin(System.Math.PI * System.Math.Clamp(t * 1.6, 0.0, 1.0)));
            brown = System.Math.Clamp(brown * 0.995 + rng.RandfRange(-1.0f, 1.0f) * 0.05, -1.0, 1.0);
            double rumble = System.Math.Sin(
                    System.Math.Tau * (36.0 + 7.0 * System.Math.Sin(System.Math.Tau * 0.9 * ((double)i / SampleRate)))
                    * ((double)i / SampleRate));
            outFrames[i] = (float)(brown * env * 1.5 + rumble * env * 0.3);
        }

        return MakeWav(outFrames);
    }
}
