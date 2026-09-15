using Godot;

namespace Dimenship.Ui;

/// <summary>
/// Builds the material the loadout stage draws frame art through: <c>projection.gdshader</c> with
/// its tint set from <see cref="ShellPalette.Projection"/>. Optional in the way
/// <see cref="ShellBackdrop"/>'s frost is: a missing shader returns null, and the caller falls
/// back to a plain palette modulate rather than failing — the art still reads, only without its
/// glow.
/// </summary>
public static class ProjectionGlow
{
    private const string ShaderPath = "res://assets/projection.gdshader";

    private static bool _resolved;
    private static Shader? _shader;

    /// <summary>A fresh material per use: uniforms are per-material, and there is nothing to share.</summary>
    public static ShaderMaterial? Create()
    {
        if (!_resolved)
        {
            _resolved = true;

            if (ResourceLoader.Exists(ShaderPath))
            {
                _shader = GD.Load<Shader>(ShaderPath);
            }
            else
            {
                GD.PushWarning($"{ShaderPath} not found; frame art is drawn without its glow.");
            }
        }

        if (_shader is null)
        {
            return null;
        }

        var material = new ShaderMaterial { Shader = _shader };
        material.SetShaderParameter("tint", ShellPalette.Projection);
        return material;
    }
}
