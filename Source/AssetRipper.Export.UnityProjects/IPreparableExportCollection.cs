namespace AssetRipper.Export.UnityProjects;

/// <summary>
/// An export collection whose expensive, CPU-bound work can be done ahead of time and in parallel.
/// </summary>
/// <remarks>
/// <see cref="ProjectExporter"/> calls the methods in this order for a window of upcoming collections:
/// <list type="number">
/// <item><see cref="LoadPreparationInput"/>, sequentially, because reading asset data uses shared, non thread-safe streams;</item>
/// <item><see cref="Prepare"/>, in parallel, which must only use the data loaded in step 1;</item>
/// <item><see cref="IExportCollection.Export"/>, sequentially, which consumes (and releases) the prepared result.</item>
/// </list>
/// File naming, GUIDs and the export container are only touched in step 3, so output is identical to a sequential export.
/// </remarks>
public interface IPreparableExportCollection : IExportCollection
{
	/// <summary>
	/// Approximate number of bytes held in memory between <see cref="LoadPreparationInput"/> and export. Used to bound the window.
	/// </summary>
	long LoadPreparationInput();

	/// <summary>
	/// Performs CPU-bound work. Must be thread-safe with respect to other collections.
	/// </summary>
	void Prepare();
}
