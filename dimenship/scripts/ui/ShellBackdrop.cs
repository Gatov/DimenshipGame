using Godot;

namespace Dimenship.Ui;

/// <summary>
/// Owns the backdrop image and the frosted-glass material panels lay over it. Both are optional:
/// if either asset is missing the shell degrades to flat fills rather than failing, so a checkout
/// without the image still runs.
/// </summary>
public static class ShellBackdrop
{
    // JPEG rather than PNG: the source is a photographic nebula with no alpha, and lossless cost
    // 5.9 MB against 0.5 MB for a quality-92 encode that is indistinguishable at 2x zoom.
    private const string TexturePath = "res://assets/shell_background.jpg";
    private const string ShaderPath = "res://assets/frosted_glass.gdshader";

    private static bool _resolved;
    private static Texture2D? _texture;
    private static Shader? _shader;

    public static Texture2D? Texture
    {
        get
        {
            Resolve();
            return _texture;
        }
    }

    /// <summary>
    /// A fresh material per pane. Uniforms are per-material, and panels are created and freed as
    /// the user switches them, so there is nothing to gain from sharing one.
    /// </summary>
    public static ShaderMaterial? CreateFrostMaterial()
    {
        Resolve();
        if (_texture is null || _shader is null)
        {
            return null;
        }

        var material = new ShaderMaterial { Shader = _shader };
        material.SetShaderParameter("backdrop", _texture);
        material.SetShaderParameter("scrim", ShellPalette.BgScrim);
        material.SetShaderParameter("tint", ShellPalette.BgGlass);
        return material;
    }

    private static void Resolve()
    {
        if (_resolved)
        {
            return;
        }

        _resolved = true;
        _texture = Load<Texture2D>(TexturePath);
        _shader = Load<Shader>(ShaderPath);
    }

    private static T? Load<T>(string path)
        where T : Resource
    {
        // Exists() first: GD.Load on a missing path logs an engine error, and a missing backdrop is
        // a cosmetic problem, not an error worth that noise.
        if (ResourceLoader.Exists(path))
        {
            return GD.Load<T>(path);
        }

        GD.PushWarning($"{path} not found; the shell falls back to flat fills.");
        return null;
    }
}
