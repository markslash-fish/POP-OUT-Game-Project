using System;
using UnityEngine;

/// <summary>
/// Put on every player (same object as Health). Holds the player's CURRENT color and tints the character with it.
/// The color is chosen by the ColorAssigner at every phase change.
/// </summary>
public class PlayerColorIdentity : MonoBehaviour
{
    public Health health;                        // auto-found
    public Renderer[] tintRenderers;             // leave empty = every visible Renderer under this object

    public PlayerColor CurrentColor { get; private set; } = PlayerColor.Red;
    public event Action<PlayerColor> ColorChanged;

    static readonly int ColorId = Shader.PropertyToID("_Color");        // Built-in / Sprites shaders
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"); // URP Lit
    MaterialPropertyBlock block;

    void Awake()
    {
        if (health == null) health = GetComponent<Health>();
        block = new MaterialPropertyBlock();

        if (tintRenderers == null || tintRenderers.Length == 0)
        {
            var list = new System.Collections.Generic.List<Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
                if (!r.enabled) continue;   // skip hidden meshes (e.g. the Animated_Body "ghost")
                list.Add(r);
            }
            tintRenderers = list.ToArray();
        }
    }

    public void SetColor(PlayerColor color)
    {
        CurrentColor = color;
        ApplyTint();
        ColorChanged?.Invoke(color);
    }

    void ApplyTint()
    {
        Color c = PlayerColorInfo.ToColor(CurrentColor);
        foreach (var r in tintRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(block);
            block.SetColor(ColorId, c);
            block.SetColor(BaseColorId, c);
            r.SetPropertyBlock(block);
        }
    }
}