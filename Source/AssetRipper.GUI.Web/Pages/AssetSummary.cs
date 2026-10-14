using AssetRipper.Assets;
using AssetRipper.Processing.Textures;
using AssetRipper.SourceGenerated.Classes.ClassID_1;
using AssetRipper.SourceGenerated.Classes.ClassID_114;
using AssetRipper.SourceGenerated.Classes.ClassID_115;
using AssetRipper.SourceGenerated.Classes.ClassID_128;
using AssetRipper.SourceGenerated.Classes.ClassID_2;
using AssetRipper.SourceGenerated.Classes.ClassID_21;
using AssetRipper.SourceGenerated.Classes.ClassID_213;
using AssetRipper.SourceGenerated.Classes.ClassID_28;
using AssetRipper.SourceGenerated.Classes.ClassID_43;
using AssetRipper.SourceGenerated.Classes.ClassID_48;
using AssetRipper.SourceGenerated.Classes.ClassID_49;
using AssetRipper.SourceGenerated.Classes.ClassID_83;
using System.Globalization;

namespace AssetRipper.GUI.Web.Pages;

/// <summary>
/// Short, human readable descriptions of assets for cards and headers.
/// </summary>
internal static class AssetSummary
{
	/// <summary>A visual category, used for icons and accent colors (CSS class <c>kind-{value}</c>).</summary>
	public static string GetKind(IUnityObjectBase asset) => asset switch
	{
		ITexture2D or ISprite or SpriteInformationObject => "image",
		IAudioClip => "audio",
		IMesh => "mesh",
		IMonoScript => "script",
		IMonoBehaviour => "behaviour",
		ITextAsset => "text",
		IMaterial => "material",
		IShader => "shader",
		IFont => "font",
		_ when asset.ClassID == 74 => "animation",
		IGameObject => "gameobject",
		IComponent => "component",
		_ when asset.ClassName.EndsWith("Settings", StringComparison.Ordinal) || asset.ClassName.EndsWith("Manager", StringComparison.Ordinal) => "settings",
		_ => "other",
	};

	public static string GetIcon(string kind) => kind switch
	{
		"image" => "🖼️",
		"audio" => "🎵",
		"mesh" => "🧊",
		"script" => "📜",
		"behaviour" => "🧠",
		"text" => "📄",
		"material" => "🎨",
		"shader" => "✨",
		"font" => "🔤",
		"animation" => "🎞️",
		"gameobject" => "🎮",
		"component" => "🧩",
		"settings" => "⚙️",
		_ => "📦",
	};

	/// <summary>One line of key facts, or null if nothing interesting is known.</summary>
	public static string? GetDetail(IUnityObjectBase asset)
	{
		try
		{
			return asset switch
			{
				ITexture2D texture => $"{texture.Width_C28}×{texture.Height_C28} · {texture.Format_C28E}",
				SpriteInformationObject info => $"{info.Texture.Width_C28}×{info.Texture.Height_C28} · {info.Sprites.Count} sprite(s)",
				ISprite sprite => $"{sprite.Rect.Width.ToString("0", CultureInfo.InvariantCulture)}×{sprite.Rect.Height.ToString("0", CultureInfo.InvariantCulture)} sprite",
				IAudioClip clip => $"{clip.Length.ToString("0.0", CultureInfo.InvariantCulture)} s · {clip.Channels} ch · {clip.Frequency} Hz",
				IMesh mesh => $"{mesh.VertexData.VertexCount:N0} vertices · {mesh.SubMeshes.Count} submesh(es)",
				IMonoScript script => string.IsNullOrEmpty(script.Namespace.String) ? script.ClassName_R.String : $"{script.Namespace}.{script.ClassName_R}",
				IMonoBehaviour behaviour when behaviour.ScriptP is { } script => script.ClassName_R.String,
				ITextAsset text => FormatBytes(text.Script_C49.Data.Length),
				IGameObject gameObject => $"{gameObject.Components.Count} component(s)",
				IMaterial material when material.Shader_C21P is { } shader => shader.GetBestName(),
				_ => null,
			};
		}
		catch
		{
			return null;
		}
	}

	public static string FormatBytes(long bytes) => bytes switch
	{
		< 1024 => $"{bytes} B",
		< 1024 * 1024 => $"{bytes / 1024.0:0.0} KB",
		< 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.0} MB",
		_ => $"{bytes / (1024.0 * 1024 * 1024):0.00} GB",
	};
}
