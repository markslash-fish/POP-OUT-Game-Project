using UnityEngine;

/// <summary>The seven player colors from the Game Design docx.</summary>
public enum PlayerColor { Red, Blue, Yellow, Green, Orange, Pink, Purple }

public static class PlayerColorInfo
{
    public static readonly PlayerColor[] All =
    {
        PlayerColor.Red, PlayerColor.Blue, PlayerColor.Yellow, PlayerColor.Green,
        PlayerColor.Orange, PlayerColor.Pink, PlayerColor.Purple
    };

    // Placeholder palette - the art team can change these
    public static Color ToColor(PlayerColor c)
    {
        switch (c)
        {
            case PlayerColor.Red: return new Color(0.92f, 0.22f, 0.22f);
            case PlayerColor.Blue: return new Color(0.25f, 0.48f, 0.96f);
            case PlayerColor.Yellow: return new Color(1.00f, 0.86f, 0.15f);
            case PlayerColor.Green: return new Color(0.25f, 0.80f, 0.35f);
            case PlayerColor.Orange: return new Color(1.00f, 0.55f, 0.10f);
            case PlayerColor.Pink: return new Color(1.00f, 0.45f, 0.75f);
            default: return new Color(0.60f, 0.30f, 0.85f);   // Purple
        }
    }

    public static string DisplayName(PlayerColor c)
    {
        return c.ToString().ToUpperInvariant();
    }
}