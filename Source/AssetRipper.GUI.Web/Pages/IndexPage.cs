using AssetRipper.Assets;
using AssetRipper.GUI.Web.Paths;

namespace AssetRipper.GUI.Web.Pages;

public sealed class IndexPage : DefaultPage
{
	public static IndexPage Instance { get; } = new();

	public override string? GetTitle() => GameFileLoader.Premium ? Localization.AssetRipperPremium : Localization.AssetRipperFree;

	public override void WriteInnerContent(TextWriter writer)
	{
		if (GameFileLoader.IsLoaded)
		{
			WriteDashboard(writer);
		}
		else
		{
			WriteWelcome(writer);
		}
		WriteSupport(writer);
	}

	private static void WriteWelcome(TextWriter writer)
	{
		using (new Div(writer).WithClass("ar-hero card text-center my-5").End())
		{
			using (new Div(writer).WithClass("card-body py-5").End())
			{
				new Div(writer).WithClass("ar-hero-icon mx-auto mb-3").Close("🧰");
				new H1(writer).WithClass("display-6 fw-bold mb-2").Close(Localization.Welcome);
				new P(writer).WithClass("text-secondary mb-4").Close(Localization.UiOpenHint);
				using (new Div(writer).WithClass("d-flex justify-content-center flex-wrap gap-2").End())
				{
					WritePostButton(writer, "/LoadFolder", "📁 " + Localization.MenuFileOpenFolder, "btn btn-primary btn-lg");
					WritePostButton(writer, "/LoadFile", "📄 " + Localization.MenuFileOpenFile, "btn btn-outline-light btn-lg");
				}
			}
		}
	}

	private static void WriteDashboard(TextWriter writer)
	{
		List<IUnityObjectBase> assets = GameFileLoader.GameBundle.FetchAssets().ToList();
		int collections = GameFileLoader.GameBundle.FetchAssetCollections().Count();
		List<(string Kind, int Count)> byKind = assets
			.GroupBy(AssetSummary.GetKind)
			.Select(g => (g.Key, g.Count()))
			.OrderByDescending(p => p.Item2)
			.ToList();

		using (new Div(writer).WithClass("ar-hero card mb-4").End())
		{
			using (new Div(writer).WithClass("card-body d-flex flex-wrap align-items-center gap-3").End())
			{
				new Div(writer).WithClass("ar-hero-icon").Close("🎮");
				using (new Div(writer).WithClass("flex-grow-1").End())
				{
					new Div(writer).WithClass("ar-card-class").Close(Localization.UiGameOverview);
					new H1(writer).WithClass("h3 mb-2").Close(string.IsNullOrEmpty(GameFileLoader.GameBundle.Name) ? Localization.ViewLoadedFiles : GameFileLoader.GameBundle.Name);
					using (new Div(writer).WithClass("d-flex flex-wrap gap-2 small").End())
					{
						Collections.ViewPage.WriteChip(writer, Localization.UiPlatform, () => writer.Write((GameFileLoader.PlatformName ?? "?").ToHtml()));
						Collections.ViewPage.WriteChip(writer, Localization.UiScriptingBackend, () => writer.Write(GameFileLoader.AssemblyManager.ScriptingBackend.ToString()));
						Collections.ViewPage.WriteChip(writer, Localization.UiUnityVersion, () => writer.Write(GameFileLoader.GameBundle.GetMaxUnityVersion().ToString().ToHtml()));
					}
				}
				using (new Div(writer).WithClass("d-flex flex-wrap gap-2").End())
				{
					PathLinking.WriteLink(writer, GameFileLoader.GameBundle, "🔎 " + Localization.ViewLoadedFiles, "btn btn-primary");
					new A(writer).WithHref("/Commands").WithClass("btn btn-success").Close("⬇️ " + Localization.UiExportProject);
				}
			}
		}

		using (new Div(writer).WithClass("row g-3 mb-4").End())
		{
			WriteStat(writer, Localization.Assets, assets.Count.ToString("N0"));
			WriteStat(writer, Localization.Collection, collections.ToString("N0"));
			WriteStat(writer, Localization.UiAssetTypes, assets.Select(a => a.ClassName).Distinct().Count().ToString("N0"));
			WriteStat(writer, "Unreadable", assets.Count(a => a is Import.AssetCreation.UnreadableObject).ToString("N0"));
		}

		using (new Div(writer).WithClass("card mb-4").End())
		{
			using (new Div(writer).WithClass("card-body").End())
			{
				new H2(writer).WithClass("h6 text-secondary text-uppercase mb-3").Close(Localization.UiAssetTypes);
				int max = byKind.Count > 0 ? byKind[0].Count : 1;
				foreach ((string kind, int count) in byKind)
				{
					using (new Div(writer).WithClass($"d-flex align-items-center gap-3 mb-2 kind-{kind}").End())
					{
						new Span(writer).WithClass("ar-mini-icon").Close(AssetSummary.GetIcon(kind));
						using (new Div(writer).WithClass("flex-grow-1").End())
						{
							using (new Div(writer).WithClass("d-flex justify-content-between small").End())
							{
								new Span(writer).WithClass("text-capitalize").Close(kind);
								new Span(writer).WithClass("text-secondary").Close(count.ToString("N0"));
							}
							using (new Div(writer).WithClass("ar-bar").End())
							{
								new Span(writer).WithStyle($"width:{System.Math.Max(2, count * 100 / max)}%;background:var(--kind)").Close();
							}
						}
					}
				}
			}
		}
	}

	private static void WriteStat(TextWriter writer, string label, string value)
	{
		using (new Div(writer).WithClass("col-6 col-md-3").End())
		{
			using (new Div(writer).WithClass("card ar-stat h-100").End())
			{
				new Div(writer).WithClass("ar-stat-value").Close(value);
				new Div(writer).WithClass("ar-stat-label").Close(label);
			}
		}
	}

	private static void WritePostButton(TextWriter writer, string url, string text, string @class)
	{
		using (new Form(writer).WithAction(url).WithMethod("post").WithClass("d-inline").End())
		{
			new Button(writer).WithType("submit").WithClass(@class).Close(text);
		}
	}

	private static void WriteSupport(TextWriter writer)
	{
		using (new Div(writer).WithClass("text-center text-secondary small mt-4").End())
		{
			if (GameFileLoader.Premium)
			{
				new P(writer).Close(Localization.AppreciationMessage);
			}
			else
			{
				new P(writer).WithClass("mb-2").Close(Localization.TippingMessage);
				using (new Div(writer).WithClass("d-flex justify-content-center flex-wrap gap-2").End())
				{
					new A(writer).WithClass("btn btn-sm btn-outline-secondary").WithNewTabAttributes().WithHref("https://paypal.me/ds5678").Close("Paypal");
					new A(writer).WithClass("btn btn-sm btn-outline-secondary").WithNewTabAttributes().WithHref("https://github.com/sponsors/ds5678").Close("GitHub Sponsors");
					new A(writer).WithClass("btn btn-sm btn-outline-secondary").WithNewTabAttributes().WithHref("https://buymeacoffee.com/assetripper").Close("Buy Me a Coffee");
					new A(writer).WithClass("btn btn-sm btn-outline-secondary").WithNewTabAttributes().WithHref("https://ko-fi.com/assetripper").Close("Ko-fi");
				}
			}
		}
	}
}
