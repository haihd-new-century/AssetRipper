using AssetRipper.Assets;
using AssetRipper.GUI.Web.Paths;
using AssetRipper.GUI.Web.Pages;

namespace AssetRipper.GUI.Web.Pages.Assets;

public sealed class ViewPage : DefaultPage
{
	public required IUnityObjectBase Asset { get; init; }
	public required AssetPath Path { get; init; }

	public override string GetTitle() => Asset.GetBestName();

	public override void WriteInnerContent(TextWriter writer)
	{
		WriteHeaderCard(writer);

		ReadOnlySpan<HtmlTab> tabs =
			[
				new InformationTab(Asset, Path),
				new AudioTab(Asset, Path),
				new ImageTab(Asset, Path),
				new ModelTab(Asset, Path),
				new TextTab(Asset, Path),
				new FontTab(Asset, Path),
				new VideoTab(Asset, Path),
				new YamlTab(Asset, Path),
				new JsonTab(Asset, Path),
				new HexTab(Asset, Path),
				new DependenciesTab(Asset),
				new DevelopmentTab(Asset),
			];

		HtmlTab.WriteNavigation(writer, tabs);
		HtmlTab.WriteContent(writer, tabs);
	}

	private void WriteHeaderCard(TextWriter writer)
	{
		string kind = AssetSummary.GetKind(Asset);
		using (new Div(writer).WithClass($"ar-hero card mb-4 kind-{kind}").End())
		{
			using (new Div(writer).WithClass("card-body d-flex flex-wrap align-items-center gap-4").End())
			{
				if (AssetAPI.HasImageData(Asset))
				{
					using (new A(writer).WithHref(AssetAPI.GetImageUrl(Path, "png")).WithNewTabAttributes().End())
					{
						new Img(writer).WithSrc(AssetAPI.GetThumbnailUrl(Path, 256)).WithAlt(GetTitle()).WithClass("ar-hero-thumb").Close();
					}
				}
				else
				{
					new Div(writer).WithClass("ar-hero-icon").Close(AssetSummary.GetIcon(kind));
				}
				using (new Div(writer).WithClass("flex-grow-1 min-w-0").End())
				{
					new Div(writer).WithClass("ar-card-class mb-1").Close(Asset.ClassName);
					new H1(writer).WithClass("h3 mb-2 text-break").Close(GetTitle());
					using (new Div(writer).WithClass("d-flex flex-wrap gap-2 small").End())
					{
						Collections.ViewPage.WriteChip(writer, Localization.Collection, () => PathLinking.WriteLink(writer, Path.CollectionPath, Asset.Collection.Name));
						Collections.ViewPage.WriteChip(writer, Localization.PathId, () => writer.Write(Asset.PathID.ToString()));
						if (AssetSummary.GetDetail(Asset) is { } detail)
						{
							Collections.ViewPage.WriteChip(writer, Localization.UiDetails, () => writer.Write(detail.ToHtml()));
						}
						if (Asset.OriginalPath is { } originalPath)
						{
							Collections.ViewPage.WriteChip(writer, Localization.OriginalPath, () => writer.Write(originalPath.ToHtml()));
						}
					}
				}
			}
		}
	}

	protected override void WriteScriptReferences(TextWriter writer)
	{
		base.WriteScriptReferences(writer);
		OnlineDependencies.Babylon.WriteScriptReference(writer);
		new Script(writer).WithSrc("/js/mesh_preview.js").Close();
	}
}
