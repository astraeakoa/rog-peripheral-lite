namespace GHelper.USB;
    public enum AuraMode : int
    {
        AuraStatic = 0,
        AuraBreathe = 1,
        AuraColorCycle = 2,
        AuraRainbow = 3,
        Star = 4,
        Rain = 5,
        Highlight = 6,
        Laser = 7,
        Ripple = 8,
        AuraStrobe = 10,
        Comet = 11,
        Flash = 12,
        HEATMAP = 20,
        GPUMODE = 21,
        AMBIENT = 22,
        BATTERY = 23,
        GRADIENT = 24,
        ZONETEST = 25,
        AUDIO = 26,
        AUDIOPULSE = 27,
    }

    public enum AuraSpeed : int
    {
        Slow = 0,
        Normal = 1,
        Fast = 2,
    }

public static class Aura { public static Dictionary<AuraSpeed,string> GetSpeeds() => new() { [AuraSpeed.Slow] = "慢", [AuraSpeed.Normal] = "正常", [AuraSpeed.Fast] = "快" }; }
