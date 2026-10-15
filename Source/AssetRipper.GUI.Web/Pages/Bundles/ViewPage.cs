using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.GUI.Web.Pages.Explorer;
using AssetRipper.GUI.Web.Pages.FailedFiles;
using AssetRipper.GUI.Web.Pages.Resources;
using AssetRipper.GUI.Web.Paths;

namespace AssetRipper.GUI.Web.Pages.Bundles;

/// <summary>
/// The game bundle explorer: a folder tree on the left and the selected folder's contents
/// (with an item preview) on the right, similar to a desktop file explorer.
/// </summary>
public sealed class ViewPage : DefaultPage
{
	public required Bundle Bundle { get; init; }
	public required BundlePath Path { get; init; }

	public override string GetTitle() => ExplorerAPI.DisplayName(Bundle);

	public override void WriteInnerContent(TextWriter writer)
	{
		Bundle root = Bundle;
		while (root.Parent is not null)
		{
			root = root.Parent;
		}
		HashSet<Bundle> openBundles = [];
		for (Bundle? b = Bundle; b is not null; b = b.Parent)
		{
			openBundles.Add(b);
		}

		using (new Div(writer).WithClass("ar-explorer").WithCustomAttribute("data-initial-url", ExplorerAPI.GetBundleUrl(Path)).End())
		{
			using (new Aside(writer).WithClass("ar-tree-pane").WithCustomAttribute("aria-label", Localization.Bundles).End())
			{
				using (new Div(writer).WithClass("ar-tree-toolbar").End())
				{
					new Input(writer).WithType("search").WithClass("form-control form-control-sm ar-tree-filter")
						.WithCustomAttribute("placeholder", Localization.Search + "…")
						.WithCustomAttribute("aria-label", Localization.Search).Close();
				}
				using (new Ul(writer).WithClass("ar-tree").WithRole("tree").End())
				{
					WriteBundleNode(writer, root, root.GetPath(), openBundles, 0);
				}
			}

			using (new Section(writer).WithClass("ar-content-pane").End())
			{
				using (new Div(writer).WithClass("ar-content").WithId("explorerContent").WithCustomAttribute("aria-live", "polite").End())
				{
					new Div(writer).WithClass("ar-loading").Close("…");
				}
				using (new Aside(writer).WithClass("ar-preview-pane").WithId("explorerPreview").End())
				{
					new Div(writer).WithClass("ar-preview-empty text-secondary").Close(Localization.UiPreview);
				}
			}
		}
	}

	private static void WriteBundleNode(TextWriter writer, Bundle bundle, BundlePath path, HashSet<Bundle> openBundles, int depth)
	{
		List<(int Index, Bundle Bundle)> children = ExplorerAPI.SortedBundles(bundle).ToList();
		List<(int Index, AssetCollection Collection)> collections = ExplorerAPI.VisibleCollections(bundle).ToList();
		bool hasChildren = children.Count + collections.Count + bundle.Resources.Count + bundle.FailedFiles.Count > 0;
		bool open = depth == 0 || openBundles.Contains(bundle);

		using (new Li(writer).WithRole("treeitem").WithClass(open ? "ar-node open" : "ar-node").End())
		{
			using (new Div(writer).WithClass("ar-node-row")
				.WithCustomAttribute("data-explorer-url", ExplorerAPI.GetBundleUrl(path))
				.WithCustomAttribute("data-search", ExplorerAPI.DisplayName(bundle).ToLowerInvariant())
				.End())
			{
				new Span(writer).WithClass(hasChildren ? "ar-caret" : "ar-caret empty").Close();
				new Span(writer).WithClass("ar-node-icon").Close(depth == 0 ? "🎮" : "📁");
				new Span(writer).WithClass("ar-node-label").Close(ExplorerAPI.DisplayName(bundle));
			}
			if (!hasChildren)
			{
				return;
			}
			using (new Ul(writer).WithRole("group").End())
			{
				foreach ((int index, Bundle child) in children)
				{
					WriteBundleNode(writer, child, path.GetChild(index), openBundles, depth + 1);
				}
				foreach ((int index, AssetCollection collection) in collections)
				{
					using (new Li(writer).WithRole("treeitem").WithClass("ar-node leaf").End())
					{
						using (new Div(writer).WithClass("ar-node-row")
							.WithCustomAttribute("data-explorer-url", ExplorerAPI.GetCollectionUrl(path.GetCollection(index)))
							.WithCustomAttribute("data-search", collection.Name.ToLowerInvariant())
							.End())
						{
							new Span(writer).WithClass("ar-caret empty").Close();
							new Span(writer).WithClass("ar-node-icon").Close(collection.IsScene ? "🌄" : "🗂️");
							new Span(writer).WithClass("ar-node-label").Close(collection.Name);
							new Span(writer).WithClass("ar-node-count").Close(collection.Count.ToString("N0"));
						}
					}
				}
				for (int i = 0; i < bundle.Resources.Count; i++)
				{
					WriteLinkLeaf(writer, "📦", bundle.Resources[i].Name, ResourceAPI.GetViewUrl(path.GetResource(i)));
				}
				for (int i = 0; i < bundle.FailedFiles.Count; i++)
				{
					WriteLinkLeaf(writer, "⚠️", bundle.FailedFiles[i].NameFixed, FailedFileAPI.GetViewUrl(path.GetFailedFile(i)));
				}
			}
		}
	}

	private static void WriteLinkLeaf(TextWriter writer, string icon, string name, string url)
	{
		using (new Li(writer).WithRole("treeitem").WithClass("ar-node leaf").End())
		{
			using (new A(writer).WithHref(url).WithClass("ar-node-row").WithCustomAttribute("data-search", name.ToLowerInvariant()).End())
			{
				new Span(writer).WithClass("ar-caret empty").Close();
				new Span(writer).WithClass("ar-node-icon").Close(icon);
				new Span(writer).WithClass("ar-node-label").Close(name);
			}
		}
	}

	protected override void WriteScriptReferences(TextWriter writer)
	{
		base.WriteScriptReferences(writer);
		new Script(writer).WithSrc("/js/explorer.js").Close();
	}
}
