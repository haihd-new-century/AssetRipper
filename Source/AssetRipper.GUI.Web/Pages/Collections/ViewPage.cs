using AssetRipper.Assets;
using AssetRipper.Assets.Collections;
using AssetRipper.GUI.Web.Pages.Assets;
using AssetRipper.GUI.Web.Paths;

namespace AssetRipper.GUI.Web.Pages.Collections;

public sealed class ViewPage : DefaultPage
{
	public required AssetCollection Collection { get; init; }
	public required CollectionPath Path { get; init; }

	public override string GetTitle() => Collection.Name;

	public override void WriteInnerContent(TextWriter writer)
	{
		WriteHeaderCard(writer);

		if (Collection.Count > 0)
		{
			WriteToolbar(writer);
			WriteGrid(writer);
			WriteTableShell(writer);
			using (new Div(writer).WithClass("text-center my-4").End())
			{
				new Button(writer).WithType("button").WithId("showMore").WithClass("btn btn-outline-secondary d-none").Close("…");
			}
			new P(writer).WithId("emptyFilterMessage").WithClass("text-secondary text-center my-5 d-none").Close("—");
		}

		if (Collection.Dependencies.Count > 1)
		{
			WriteDependencies(writer);
		}
	}

	private void WriteHeaderCard(TextWriter writer)
	{
		using (new Div(writer).WithClass("ar-hero card mb-4").End())
		{
			using (new Div(writer).WithClass("card-body d-flex flex-wrap align-items-center gap-3").End())
			{
				new Div(writer).WithClass("ar-hero-icon").Close(Collection.IsScene ? "🌄" : "🗂️");
				using (new Div(writer).WithClass("flex-grow-1").End())
				{
					new H1(writer).WithClass("h3 mb-1 text-break").Close(GetTitle());
					using (new Div(writer).WithClass("d-flex flex-wrap gap-2 small").End())
					{
						WriteChip(writer, Localization.Bundle, () => PathLinking.WriteLink(writer, Path.BundlePath, Collection.Bundle.Name));
						if (Collection.IsScene)
						{
							WriteChip(writer, Localization.Scene, () => PathLinking.WriteLink(writer, (ScenePath)Path, Collection.Scene.Name));
						}
						WriteChip(writer, Localization.Assets, () => writer.Write(Collection.Count.ToString("N0")));
						WriteChip(writer, Localization.UiUnityVersion, () => writer.Write(Collection.Version.ToString().ToHtml()));
					}
				}
			}
		}
	}

	internal static void WriteChip(TextWriter writer, string label, Action writeValue)
	{
		using (new Span(writer).WithClass("ar-chip").End())
		{
			new Span(writer).WithClass("ar-chip-label").Close(label);
			writeValue();
		}
	}

	private void WriteToolbar(TextWriter writer)
	{
		List<(string ClassName, int Count)> classes = Collection
			.GroupBy(a => a.ClassName)
			.Select(g => (g.Key, g.Count()))
			.OrderBy(c => c.Key, StringComparer.Ordinal)
			.ToList();

		using (new Div(writer).WithClass("ar-toolbar d-flex flex-wrap gap-2 align-items-center mb-3").End())
		{
			new Input(writer)
				.WithType("search")
				.WithId("assetFilter")
				.WithClass("form-control ar-search")
				.WithCustomAttribute("placeholder", Localization.UiFilterPlaceholder)
				.WithCustomAttribute("aria-label", Localization.Search)
				.Close();

			using (new Select(writer).WithId("classFilter").WithClass("form-select ar-select").WithCustomAttribute("aria-label", Localization.Class).End())
			{
				new Option(writer).WithValue(string.Empty).Close($"{Localization.All} ({Collection.Count:N0})");
				foreach ((string className, int count) in classes)
				{
					new Option(writer).WithValue(className).Close($"{className} ({count:N0})");
				}
			}

			using (new Div(writer).WithClass("btn-group ms-auto").WithRole("group").End())
			{
				new Button(writer).WithType("button").WithClass("btn btn-outline-secondary active").WithCustomAttribute("data-view", "grid").Close("▦ " + Localization.UiViewGrid);
				new Button(writer).WithType("button").WithClass("btn btn-outline-secondary").WithCustomAttribute("data-view", "table").Close("☰ " + Localization.UiViewTable);
			}
			new Span(writer).WithId("filterCount").WithClass("text-secondary small w-100").WithCustomAttribute("data-template", Localization.UiShownOf).Close();
		}
	}

	private void WriteGrid(TextWriter writer)
	{
		using (new Div(writer).WithId("assetGrid").WithClass("ar-grid").End())
		{
			foreach (IUnityObjectBase asset in Collection)
			{
				AssetPath assetPath = Path.GetAsset(asset.PathID);
				string kind = AssetSummary.GetKind(asset);
				string name = asset.GetBestName();
				using (new A(writer)
					.WithHref(AssetAPI.GetViewUrl(assetPath))
					.WithClass($"ar-card kind-{kind}")
					.WithCustomAttribute("data-class", asset.ClassName)
					.WithCustomAttribute("data-kind", kind)
					.WithCustomAttribute("data-pathid", asset.PathID.ToString())
					.WithCustomAttribute("data-search", $"{name} {asset.ClassName} {asset.PathID}".ToLowerInvariant())
					.WithCustomAttribute("title", name)
					.End())
				{
					using (new Div(writer).WithClass("ar-thumb").End())
					{
						if (AssetAPI.HasImageData(asset))
						{
							new Img(writer)
								.WithCustomAttribute("data-src", AssetAPI.GetThumbnailUrl(assetPath))
								.WithAlt(name)
								.WithCustomAttribute("loading", "lazy")
								.WithCustomAttribute("decoding", "async")
								.WithCustomAttribute("onerror", "this.replaceWith(Object.assign(document.createElement('span'),{className:'ar-thumb-icon',textContent:'🖼️'}))")
								.Close();
						}
						else
						{
							new Span(writer).WithClass("ar-thumb-icon").Close(AssetSummary.GetIcon(kind));
						}
					}
					using (new Div(writer).WithClass("ar-card-body").End())
					{
						new Div(writer).WithClass("ar-card-title").Close(name);
						new Div(writer).WithClass("ar-card-class").Close(asset.ClassName);
						string? detail = AssetSummary.GetDetail(asset);
						new Div(writer).WithClass("ar-card-detail").Close(detail ?? $"#{asset.PathID}");
					}
				}
			}
		}
	}

	/// <summary>
	/// The table view is built in the browser from the cards' data attributes, so large collections are not rendered twice.
	/// </summary>
	private static void WriteTableShell(TextWriter writer)
	{
		using (new Div(writer).WithId("assetTableWrapper").WithClass("table-responsive d-none").End())
		{
			using (new Table(writer).WithClass("table table-hover align-middle ar-table").End())
			{
				using (new Thead(writer).End())
				{
					using (new Tr(writer).End())
					{
						new Th(writer).WithClass("ar-col-thumb").Close(Localization.UiPreview);
						new Th(writer).Close(Localization.Name);
						new Th(writer).Close(Localization.Class);
						new Th(writer).Close(Localization.UiDetails);
						new Th(writer).WithClass("text-end").Close(Localization.PathId);
					}
				}
				new Tbody(writer).WithId("assetsTable").Close();
			}
		}
	}

	private void WriteDependencies(TextWriter writer)
	{
		new H2(writer).WithClass("h5 mt-5").Close(Localization.AssetTabDependencies);
		using (new Div(writer).WithClass("table-responsive").End())
		{
			using (new Table(writer).WithClass("table table-sm ar-table").End())
			{
				using (new Thead(writer).End())
				{
					using (new Tr(writer).End())
					{
						new Th(writer).Close(Localization.FileId);
						new Th(writer).Close(Localization.Name);
					}
				}
				using (new Tbody(writer).End())
				{
					for (int i = 1; i < Collection.Dependencies.Count; i++)
					{
						AssetCollection? dependency = Collection.Dependencies[i];
						if (dependency is null)
						{
							continue;
						}
						using (new Tr(writer).End())
						{
							new Td(writer).Close(i.ToString());
							using (new Td(writer).End())
							{
								PathLinking.WriteLink(writer, dependency);
							}
						}
					}
				}
			}
		}
	}

	protected override void WriteScriptReferences(TextWriter writer)
	{
		base.WriteScriptReferences(writer);
		new Script(writer).WithSrc("/js/asset_browser.js").Close();
	}
}
