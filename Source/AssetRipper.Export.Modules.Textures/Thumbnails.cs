using AssetRipper.TextureDecoder.Rgb.Formats;

namespace AssetRipper.Export.Modules.Textures;

/// <summary>
/// Creates small preview images for the web interface.
/// </summary>
public static class Thumbnails
{
	/// <summary>
	/// Downscales the first layer of <paramref name="source"/> so that its longest side is at most <paramref name="maxSize"/>,
	/// using a box filter (average of all covered source pixels). Images that are already small enough are only converted.
	/// </summary>
	public static DirectBitmap<ColorRGBA<byte>, byte> Create(DirectBitmap source, int maxSize)
	{
		ArgumentOutOfRangeException.ThrowIfLessThan(maxSize, 1);
		if (source.IsEmpty)
		{
			return new DirectBitmap<ColorRGBA<byte>, byte>(1, 1, 1);
		}

		DirectBitmap layer = source.Depth > 1 ? source.GetLayer(0) : source;
		int width = layer.Width;
		int height = layer.Height;
		byte[] rgba = layer.ToRgba32();

		double scale = Math.Min(1.0, (double)maxSize / Math.Max(width, height));
		int targetWidth = Math.Max(1, (int)Math.Round(width * scale));
		int targetHeight = Math.Max(1, (int)Math.Round(height * scale));
		if (targetWidth == width && targetHeight == height)
		{
			return new DirectBitmap<ColorRGBA<byte>, byte>(width, height, 1, rgba);
		}

		byte[] result = new byte[targetWidth * targetHeight * 4];
		for (int ty = 0; ty < targetHeight; ty++)
		{
			int y0 = ty * height / targetHeight;
			int y1 = Math.Max(y0 + 1, (ty + 1) * height / targetHeight);
			for (int tx = 0; tx < targetWidth; tx++)
			{
				int x0 = tx * width / targetWidth;
				int x1 = Math.Max(x0 + 1, (tx + 1) * width / targetWidth);
				long r = 0, g = 0, b = 0, a = 0;
				for (int y = y0; y < y1; y++)
				{
					int row = y * width;
					for (int x = x0; x < x1; x++)
					{
						int i = (row + x) * 4;
						r += rgba[i];
						g += rgba[i + 1];
						b += rgba[i + 2];
						a += rgba[i + 3];
					}
				}
				long count = (long)(y1 - y0) * (x1 - x0);
				int o = (ty * targetWidth + tx) * 4;
				result[o] = (byte)(r / count);
				result[o + 1] = (byte)(g / count);
				result[o + 2] = (byte)(b / count);
				result[o + 3] = (byte)(a / count);
			}
		}
		return new DirectBitmap<ColorRGBA<byte>, byte>(targetWidth, targetHeight, 1, result);
	}
}
