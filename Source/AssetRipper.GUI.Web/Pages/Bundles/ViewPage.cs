using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.GUI.Web.Pages.Assets;
using AssetRipper.GUI.Web.Pages.Collections;
using AssetRipper.GUI.Web.Pages.FailedFiles;
using AssetRipper.GUI.Web.Pages.Resources;
using AssetRipper.GUI.Web.Paths;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.ResourceFiles;

namespace AssetRipper.GUI.Web.Pages.Bundles;

/// <summary>
/// A bundle shown as a grid of cards: child bundles, asset collections (with a cover image when they contain images),
/// resource files and files that failed to load. Clicking a card opens it.
/// </summary>
public sealed class ViewPage : DefaultPage
{
	public required Bundle Bundle { get; init; }
	public required BundlePath Path { get; init; }

	public override string GetTitle() => DisplayName(Bundle);

	private static string DisplayName(Bundle bundle) => string.IsNullOrEmpty(bundle.Name) ? "Game" : bundle.Name;

	private sealed record Item(string Kind, string Icon, string Name, string Detail, string Url, string? CoverUrl);

	public override void WriteInnerContent(TextWriter writer)
	{
		List<Item> items = CollectItems();
		WriteBreadcrumb(writer);
		WriteHeader(writer, items);

		if (items.Count == 0)
		{
			new P(writer).WithClass("text-secondary text-center my-5").Close("—");
			return;
		}

		using (new Div(writer).WithClass("ar-toolbar d-flex flex-wrap gap-2 align-items-center mb-3").End())
		{
			new Input(writer).WithType("search").WithId("bundleFilter").WithClass("form-control ar-search")
				.WithCustomAttribute("placeholder", Localization.Search + "…")
				.WithCustomAttribute("aria-label", Localization.Search).Close();
			using (new Select(writer).WithId("bundleKind").WithClass("form-select ar-select").WithCustomAttribute("aria-label", Localization.Class).End())
			{
				new Option(writer).WithValue("").Close($"{Localization.All} ({items.Count:N0})");
				foreach (IGrouping<string, Item> group in items.GroupBy(i => i.Kind))
				{
					new Option(writer).WithValue(group.Key).Close($"{KindLabel(group.Key)} ({group.Count():N0})");
				}
			}
			new Span(writer).WithId("bundleCount").WithClass("text-secondary small ms-auto").Close();
		}

		using (new Div(writer).WithId("bundleGrid").WithClass("ar-grid").End())
		{
			foreach (Item item in items)
			{
				using (new A(writer)
					.WithHref(item.Url)
					.WithClass($"ar-card kind-{item.Kind}")
					.WithCustomAttribute("data-kind", item.Kind)
					.WithCustomAttribute("data-search", item.Name.ToLowerInvariant())
					.WithCustomAttribute("title", item.Name)
					.End())
				{
					using (new Div(writer).WithClass(item.CoverUrl is null ? "ar-thumb" : "ar-thumb ar-thumb-cover").End())
					{
						if (item.CoverUrl is not null)
						{
							new Img(writer).WithSrc(item.CoverUrl).WithAlt("").WithCustomAttribute("loading", "lazy").WithCustomAttribute("decoding", "async").Close();
							new Span(writer).WithClass("ar-cover-badge").Close(item.Icon);
						}
						else
						{
							new Span(writer).WithClass("ar-thumb-icon").Close(item.Icon);
						}
					}
					using (new Div(writer).WithClass("ar-card-body").End())
					{
						new Div(writer).WithClass("ar-card-title").Close(item.Name);
						new Div(writer).WithClass("ar-card-class").Close(KindLabel(item.Kind));
						new Div(writer).WithClass("ar-card-detail").Close(item.Detail);
					}
				}
			}
		}
		new P(writer).WithId("bundleEmpty").WithClass("text-secondary text-center my-5 d-none").Close("—");
	}

	private List<Item> CollectItems()
	{
		List<Item> items = [];
		foreach ((int index, Bundle child) in Bundle.Bundles.Select((b, i) => (i, b)).OrderBy(p => p.b.Name, StringComparer.Ordinal))
		{
			int collections = child.Collections.Count(c => c.Count > 0 || c is SerializedAssetCollection);
			int bundles = child.Bundles.Count;
			string detail = bundles > 0 ? $"{bundles:N0} bundle(s) · {collections:N0} collection(s)" : $"{collections:N0} collection(s)";
			items.Add(new Item("bundle", "📁", DisplayName(child), detail, BundleAPI.GetViewUrl(Path.GetChild(index)), null));
		}
		foreach ((int index, AssetCollection collection) in Bundle.Collections.Select((c, i) => (i, c))
			.Where(p => p.c.Count > 0 || p.c is SerializedAssetCollection)
			.OrderBy(p => p.c.Name, StringComparer.Ordinal))
		{
			CollectionPath collectionPath = Path.GetCollection(index);
			IUnityObjectBase? cover = collection.FirstOrDefault(AssetAPI.HasImageData);
			string? coverUrl = cover is null ? null : AssetAPI.GetThumbnailUrl(collectionPath.GetAsset(cover.PathID), 256);
			string kind = collection.IsScene ? "scene" : "collection";
			items.Add(new Item(kind, collection.IsScene ? "🌄" : "🗂️", collection.Name,
				$"{collection.Count:N0} asset(s) · {collection.Version}", CollectionAPI.GetViewUrl(collectionPath), coverUrl));
		}
		foreach ((int index, ResourceFile resource) in Bundle.Resources.Select((r, i) => (i, r)).OrderBy(p => p.r.Name, StringComparer.Ordinal))
		{
			items.Add(new Item("resource", "📦", resource.Name, AssetSummary.FormatBytes(resource.Stream.Length), ResourceAPI.GetViewUrl(Path.GetResource(index)), null));
		}
		foreach ((int index, FailedFile failed) in Bundle.FailedFiles.Select((f, i) => (i, f)).OrderBy(p => p.f.NameFixed, StringComparer.Ordinal))
		{
			items.Add(new Item("failed", "⚠️", failed.NameFixed, Localization.FailedFiles, FailedFileAPI.GetViewUrl(Path.GetFailedFile(index)), null));
		}
		return items;
	}

	private static string KindLabel(string kind) => kind switch
	{
		"bundle" => Localization.Bundles,
		"scene" => Localization.Scene,
		"collection" => Localization.Collections,
		"resource" => Localization.Resources,
		"failed" => Localization.FailedFiles,
		_ => kind,
	};

	private void WriteBreadcrumb(TextWriter writer)
	{
		List<(Bundle Bundle, BundlePath Path)> chain = [];
		BundlePath path = Path;
		for (Bundle? b = Bundle; b is not null; b = b.Parent)
		{
			chain.Add((b, path));
			path = path.Parent;
		}
		if (chain.Count <= 1)
		{
			return;
		}
		chain.Reverse();
		using (new Nav(writer).WithCustomAttribute("aria-label", "breadcrumb").End())
		{
			using (new Ol(writer).WithClass("ar-crumbs").End())
			{
				for (int i = 0; i < chain.Count; i++)
				{
					if (i == chain.Count - 1)
					{
						new Li(writer).WithClass("active").Close(DisplayName(chain[i].Bundle));
					}
					else
					{
						using (new Li(writer).End())
						{
							new A(writer).WithHref(BundleAPI.GetViewUrl(chain[i].Path)).Close(DisplayName(chain[i].Bundle));
						}
					}
				}
			}
		}
	}

	private void WriteHeader(TextWriter writer, List<Item> items)
	{
		int assetCount = Bundle.Collections.Sum(c => c.Count);
		using (new Div(writer).WithClass("ar-hero card mb-4").End())
		{
			using (new Div(writer).WithClass("card-body d-flex flex-wrap align-items-center gap-3").End())
			{
				new Div(writer).WithClass("ar-hero-icon").Close(Bundle.Parent is null ? "🎮" : "📁");
				using (new Div(writer).WithClass("flex-grow-1").End())
				{
					new H1(writer).WithClass("h3 mb-2 text-break").Close(GetTitle());
					using (new Div(writer).WithClass("d-flex flex-wrap gap-2 small").End())
					{
						Collections.ViewPage.WriteChip(writer, Localization.Bundles, () => writer.Write(items.Count(i => i.Kind == "bundle").ToString("N0")));
						Collections.ViewPage.WriteChip(writer, Localization.Collections, () => writer.Write(items.Count(i => i.Kind is "collection" or "scene").ToString("N0")));
						Collections.ViewPage.WriteChip(writer, Localization.Assets, () => writer.Write(assetCount.ToString("N0")));
						if (Bundle.Resources.Count > 0)
						{
							Collections.ViewPage.WriteChip(writer, Localization.Resources, () => writer.Write(Bundle.Resources.Count.ToString("N0")));
						}
						if (Bundle.FailedFiles.Count > 0)
						{
							Collections.ViewPage.WriteChip(writer, Localization.FailedFiles, () => writer.Write(Bundle.FailedFiles.Count.ToString("N0")));
						}
					}
				}
			}
		}
	}

	protected override void WriteScriptReferences(TextWriter writer)
	{
		base.WriteScriptReferences(writer);
		using (new Script(writer).End())
		{
			writer.Write("""
				(function () {
					const grid = document.getElementById('bundleGrid');
					if (!grid) { return; }
					const cards = Array.from(grid.querySelectorAll('.ar-card'));
					const filter = document.getElementById('bundleFilter');
					const kind = document.getElementById('bundleKind');
					const count = document.getElementById('bundleCount');
					const empty = document.getElementById('bundleEmpty');
					function apply() {
						const text = filter.value.trim().toLowerCase();
						const k = kind.value;
						let shown = 0;
						cards.forEach(function (card) {
							const visible = (!k || card.dataset.kind === k) && (!text || card.dataset.search.indexOf(text) !== -1);
							card.classList.toggle('d-none', !visible);
							if (visible) { shown++; }
						});
						count.textContent = shown.toLocaleString() + ' / ' + cards.length.toLocaleString();
						empty.classList.toggle('d-none', shown !== 0);
					}
					filter.addEventListener('input', apply);
					kind.addEventListener('change', apply);
					apply();
				})();
				""");
		}
	}
}
