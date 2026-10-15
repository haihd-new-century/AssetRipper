using AssetRipper.Assets;
using AssetRipper.Assets.Bundles;
using AssetRipper.Assets.Collections;
using AssetRipper.GUI.Web.Pages.Assets;
using AssetRipper.GUI.Web.Pages.Bundles;
using AssetRipper.GUI.Web.Pages.Collections;
using AssetRipper.GUI.Web.Pages.FailedFiles;
using AssetRipper.GUI.Web.Pages.Resources;
using AssetRipper.GUI.Web.Paths;
using AssetRipper.IO.Files;
using AssetRipper.IO.Files.ResourceFiles;
using AssetRipper.Web.Extensions;
using Microsoft.AspNetCore.Http;

namespace AssetRipper.GUI.Web.Pages.Explorer;

/// <summary>
/// HTML fragments for the two-pane explorer (tree on the left, contents and preview on the right).
/// </summary>
internal static class ExplorerAPI
{
	public static class Urls
	{
		public const string Base = "/Explorer";
		public const string Bundle = Base + "/Bundle";
		public const string Collection = Base + "/Collection";
		public const string Preview = Base + "/Preview";
	}

	private const string PathKey = "Path";

	public static string GetBundleUrl(BundlePath path) => $"{Urls.Bundle}?{PathKey}={path.ToJson().ToUrl()}";
	public static string GetCollectionUrl(CollectionPath path) => $"{Urls.Collection}?{PathKey}={path.ToJson().ToUrl()}";
	public static string GetPreviewUrl(AssetPath path) => $"{Urls.Preview}?{PathKey}={path.ToJson().ToUrl()}";

	public static Task GetBundle(HttpContext context)
	{
		if (!TryGetJson(context, out string? json, out Task? failure))
		{
			return failure;
		}
		BundlePath path;
		try { path = BundlePath.FromJson(json); } catch (Exception ex) { return context.Response.NotFound(ex.Message); }
		if (!GameFileLoader.GameBundle.TryGetBundle(path, out Bundle? bundle))
		{
			return context.Response.NotFound("Bundle not found.");
		}
		return WriteFragment(context, writer => WriteBundleContent(writer, bundle, path));
	}

	public static Task GetCollection(HttpContext context)
	{
		if (!TryGetJson(context, out string? json, out Task? failure))
		{
			return failure;
		}
		CollectionPath path;
		try { path = CollectionPath.FromJson(json); } catch (Exception ex) { return context.Response.NotFound(ex.Message); }
		if (!GameFileLoader.GameBundle.TryGetCollection(path, out AssetCollection? collection))
		{
			return context.Response.NotFound("Collection not found.");
		}
		return WriteFragment(context, writer => WriteCollectionContent(writer, collection, path));
	}

	public static Task GetPreview(HttpContext context)
	{
		if (!TryGetJson(context, out string? json, out Task? failure))
		{
			return failure;
		}
		AssetPath path;
		try { path = AssetPath.FromJson(json); } catch (Exception ex) { return context.Response.NotFound(ex.Message); }
		if (!GameFileLoader.GameBundle.TryGetAsset(path, out IUnityObjectBase? asset))
		{
			return context.Response.NotFound("Asset not found.");
		}
		return WriteFragment(context, writer => WritePreview(writer, asset, path));
	}

	private static bool TryGetJson(HttpContext context, [NotNullWhen(true)] out string? json, [NotNullWhen(false)] out Task? failure)
	{
		context.Response.DisableCaching();
		if (!GameFileLoader.IsLoaded)
		{
			json = null;
			failure = context.Response.NotFound("No files loaded.");
			return false;
		}
		if (!context.Request.Query.TryGetValue(PathKey, out var values) || string.IsNullOrEmpty(values.ToString()))
		{
			json = null;
			failure = context.Response.NotFound("The path must be included in the request.");
			return false;
		}
		json = values.ToString();
		failure = null;
		return true;
	}

	private static async Task WriteFragment(HttpContext context, Action<TextWriter> write)
	{
		context.Response.ContentType = "text/html; charset=utf-8";
		using StringWriter writer = new();
		write(writer);
		await context.Response.WriteAsync(writer.ToString());
	}

	/// <summary>Breadcrumb from the root bundle to <paramref name="bundle"/>, plus an optional final item.</summary>
	internal static void WriteBreadcrumb(TextWriter writer, Bundle bundle, string? last)
	{
		List<Bundle> chain = [];
		for (Bundle? b = bundle; b is not null; b = b.Parent)
		{
			chain.Add(b);
		}
		chain.Reverse();
		using (new Nav(writer).WithCustomAttribute("aria-label", "breadcrumb").End())
		{
			using (new Ol(writer).WithClass("ar-crumbs").End())
			{
				foreach (Bundle b in chain)
				{
					using (new Li(writer).End())
					{
						new A(writer)
							.WithHref("#")
							.WithCustomAttribute("data-explorer-url", GetBundleUrl(b.GetPath()))
							.Close(DisplayName(b));
					}
				}
				if (last is not null)
				{
					new Li(writer).WithClass("active").Close(last);
				}
			}
		}
	}

	internal static string DisplayName(Bundle bundle) => string.IsNullOrEmpty(bundle.Name) ? "Game" : bundle.Name;

	internal static IEnumerable<(int Index, AssetCollection Collection)> VisibleCollections(Bundle bundle)
	{
		return bundle.Collections
			.Select((collection, index) => (index, collection))
			.Where(p => p.collection.Count > 0 || p.collection is SerializedAssetCollection)
			.OrderBy(p => p.collection.Name, StringComparer.Ordinal);
	}

	internal static IEnumerable<(int Index, Bundle Bundle)> SortedBundles(Bundle bundle)
	{
		return bundle.Bundles.Select((child, index) => (index, child)).OrderBy(p => p.child.Name, StringComparer.Ordinal);
	}

	private static void WriteBundleContent(TextWriter writer, Bundle bundle, BundlePath path)
	{
		WriteBreadcrumb(writer, bundle, null);
		int folders = bundle.Bundles.Count + VisibleCollections(bundle).Count() + bundle.Resources.Count + bundle.FailedFiles.Count;
		using (new Div(writer).WithClass("ar-pane-header").End())
		{
			new H2(writer).WithClass("h5 mb-0 text-break").Close(DisplayName(bundle));
			new Span(writer).WithClass("text-secondary small").Close($"{folders:N0} item(s)");
		}

		if (folders == 0)
		{
			new P(writer).WithClass("text-secondary p-4").Close("—");
			return;
		}

		using (new Div(writer).WithClass("ar-tiles").End())
		{
			foreach ((int index, Bundle child) in SortedBundles(bundle))
			{
				int count = child.Bundles.Count + child.Collections.Count;
				WriteTile(writer, "📁", DisplayName(child), $"{count:N0} item(s)", GetBundleUrl(path.GetChild(index)), BundleAPI.GetViewUrl(path.GetChild(index)), "bundle");
			}
			foreach ((int index, AssetCollection collection) in VisibleCollections(bundle))
			{
				CollectionPath collectionPath = path.GetCollection(index);
				WriteTile(writer, collection.IsScene ? "🌄" : "🗂️", collection.Name, $"{collection.Count:N0} asset(s) · {collection.Version}",
					GetCollectionUrl(collectionPath), CollectionAPI.GetViewUrl(collectionPath), "collection");
			}
			for (int i = 0; i < bundle.Resources.Count; i++)
			{
				ResourceFile resource = bundle.Resources[i];
				string url = ResourceAPI.GetViewUrl(path.GetResource(i));
				WriteTile(writer, "📦", resource.Name, AssetSummary.FormatBytes(resource.Stream.Length), null, url, "resource");
			}
			for (int i = 0; i < bundle.FailedFiles.Count; i++)
			{
				FailedFile failed = bundle.FailedFiles[i];
				WriteTile(writer, "⚠️", failed.NameFixed, Localization.FailedFiles, null, FailedFileAPI.GetViewUrl(path.GetFailedFile(i)), "failed");
			}
		}
	}

	private static void WriteTile(TextWriter writer, string icon, string name, string detail, string? explorerUrl, string openUrl, string kind)
	{
		A link = new A(writer)
			.WithHref(openUrl)
			.WithClass($"ar-tile ar-tile-{kind}")
			.WithCustomAttribute("title", name);
		if (explorerUrl is not null)
		{
			link = link.WithCustomAttribute("data-explorer-url", explorerUrl);
		}
		using (link.End())
		{
			new Span(writer).WithClass("ar-tile-icon").Close(icon);
			using (new Span(writer).WithClass("ar-tile-text").End())
			{
				new Span(writer).WithClass("ar-tile-name").Close(name);
				new Span(writer).WithClass("ar-tile-detail").Close(detail);
			}
		}
	}

	private static void WriteCollectionContent(TextWriter writer, AssetCollection collection, CollectionPath path)
	{
		WriteBreadcrumb(writer, collection.Bundle, collection.Name);
		using (new Div(writer).WithClass("ar-pane-header").End())
		{
			new H2(writer).WithClass("h5 mb-0 text-break").Close(collection.Name);
			using (new Div(writer).WithClass("d-flex gap-2 align-items-center").End())
			{
				new Span(writer).WithClass("text-secondary small").Close($"{collection.Count:N0} asset(s) · {collection.Version}");
				new A(writer).WithHref(CollectionAPI.GetViewUrl(path)).WithClass("btn btn-sm btn-outline-secondary").Close("↗");
			}
		}

		List<(string ClassName, int Count)> classes = collection.GroupBy(a => a.ClassName)
			.Select(g => (g.Key, g.Count())).OrderBy(c => c.Key, StringComparer.Ordinal).ToList();

		using (new Div(writer).WithClass("ar-pane-toolbar").End())
		{
			new Input(writer).WithType("search").WithClass("form-control form-control-sm ar-explorer-filter")
				.WithCustomAttribute("placeholder", Localization.UiFilterPlaceholder)
				.WithCustomAttribute("aria-label", Localization.Search).Close();
			using (new Select(writer).WithClass("form-select form-select-sm ar-explorer-class").WithCustomAttribute("aria-label", Localization.Class).End())
			{
				new Option(writer).WithValue(string.Empty).Close($"{Localization.All} ({collection.Count:N0})");
				foreach ((string className, int count) in classes)
				{
					new Option(writer).WithValue(className).Close($"{className} ({count:N0})");
				}
			}
			using (new Div(writer).WithClass("btn-group btn-group-sm").WithRole("group").End())
			{
				new Button(writer).WithType("button").WithClass("btn btn-outline-secondary").WithCustomAttribute("data-layout", "icons").WithCustomAttribute("title", Localization.UiViewGrid).Close("▦");
				new Button(writer).WithType("button").WithClass("btn btn-outline-secondary").WithCustomAttribute("data-layout", "details").WithCustomAttribute("title", Localization.UiViewTable).Close("☰");
			}
			new Span(writer).WithClass("ar-explorer-count text-secondary small ms-auto").WithCustomAttribute("data-template", Localization.UiShownOf).Close();
		}

		using (new Div(writer).WithClass("ar-items layout-icons").End())
		{
			// Header row, only shown in the "details" layout.
			using (new Div(writer).WithClass("ar-item ar-item-header").End())
			{
				new Span(writer).WithClass("ar-item-thumb").Close();
				new Span(writer).WithClass("ar-item-name").Close(Localization.Name);
				new Span(writer).WithClass("ar-item-class").Close(Localization.Class);
				new Span(writer).WithClass("ar-item-detail").Close(Localization.UiDetails);
				new Span(writer).WithClass("ar-item-id").Close(Localization.PathId);
			}
			foreach (IUnityObjectBase asset in collection)
			{
				AssetPath assetPath = path.GetAsset(asset.PathID);
				string kind = AssetSummary.GetKind(asset);
				string name = asset.GetBestName();
				using (new A(writer)
					.WithHref(AssetAPI.GetViewUrl(assetPath))
					.WithClass($"ar-item kind-{kind}")
					.WithCustomAttribute("data-preview-url", GetPreviewUrl(assetPath))
					.WithCustomAttribute("data-class", asset.ClassName)
					.WithCustomAttribute("data-search", $"{name} {asset.ClassName} {asset.PathID}".ToLowerInvariant())
					.WithCustomAttribute("title", name)
					.End())
				{
					using (new Span(writer).WithClass("ar-item-thumb").End())
					{
						if (AssetAPI.HasImageData(asset))
						{
							new Img(writer).WithCustomAttribute("data-src", AssetAPI.GetThumbnailUrl(assetPath)).WithAlt("").WithCustomAttribute("loading", "lazy").Close();
						}
						else
						{
							new Span(writer).WithClass("ar-item-icon").Close(AssetSummary.GetIcon(kind));
						}
					}
					new Span(writer).WithClass("ar-item-name").Close(name);
					new Span(writer).WithClass("ar-item-class").Close(asset.ClassName);
					new Span(writer).WithClass("ar-item-detail").Close(AssetSummary.GetDetail(asset) ?? "");
					new Span(writer).WithClass("ar-item-id").Close(asset.PathID.ToString());
				}
			}
		}
		new Button(writer).WithType("button").WithClass("btn btn-sm btn-outline-secondary ar-explorer-more d-none mx-auto my-3").Close("…");
	}

	private static void WritePreview(TextWriter writer, IUnityObjectBase asset, AssetPath path)
	{
		string kind = AssetSummary.GetKind(asset);
		using (new Div(writer).WithClass($"ar-preview-body kind-{kind}").End())
		{
			using (new Div(writer).WithClass("ar-preview-media").End())
			{
				if (AssetAPI.HasImageData(asset))
				{
					new Img(writer).WithSrc(AssetAPI.GetThumbnailUrl(path, 384)).WithAlt(asset.GetBestName()).Close();
				}
				else
				{
					new Span(writer).WithClass("ar-preview-icon").Close(AssetSummary.GetIcon(kind));
				}
			}
			new Div(writer).WithClass("ar-card-class").Close(asset.ClassName);
			new H3(writer).WithClass("h6 text-break mb-3").Close(asset.GetBestName());
			using (new Dl(writer).WithClass("ar-props").End())
			{
				WriteProperty(writer, Localization.PathId, asset.PathID.ToString());
				WriteProperty(writer, Localization.Collection, asset.Collection.Name);
				WriteProperty(writer, Localization.ClassIdTypeNumber, asset.ClassID.ToString());
				if (AssetSummary.GetDetail(asset) is { } detail)
				{
					WriteProperty(writer, Localization.UiDetails, detail);
				}
				if (asset.OriginalPath is { } originalPath)
				{
					WriteProperty(writer, Localization.OriginalPath, originalPath);
				}
				if (!string.IsNullOrEmpty(asset.AssetBundleName))
				{
					WriteProperty(writer, Localization.AssetBundleName, asset.AssetBundleName);
				}
			}
			using (new Div(writer).WithClass("d-grid gap-2").End())
			{
				new A(writer).WithHref(AssetAPI.GetViewUrl(path)).WithClass("btn btn-primary btn-sm").Close("↗ " + Localization.UiDetails);
				if (AssetAPI.HasImageData(asset))
				{
					new A(writer).WithHref(AssetAPI.GetImageUrl(path, "png")).WithNewTabAttributes().WithClass("btn btn-outline-secondary btn-sm").Close("🖼️ PNG");
				}
			}
		}
	}

	private static void WriteProperty(TextWriter writer, string name, string value)
	{
		new Dt(writer).Close(name);
		new Dd(writer).WithClass("text-break").Close(value);
	}
}
