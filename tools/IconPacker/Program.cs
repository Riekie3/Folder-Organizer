using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IconPacker;

internal static class Program
{
    private static void Main(string[] args)
    {
        string inputPath = args.Length > 0
            ? args[0]
            : @"C:\Users\rieki\.gemini\antigravity\brain\f360f12c-4559-498c-a2f0-44fff67da1fa\glassfolders_app_icon_v3_1793995844856.jpg";

        string assetsDir = @"D:\Rie\Antigravity\Folder Organizer\assets";
        Directory.CreateDirectory(assetsDir);

        string outputPng = Path.Combine(assetsDir, "app-icon.png");
        string outputIco = Path.Combine(assetsDir, "app.ico");

        Console.WriteLine($"Loading source: {inputPath}");
        var uri = new Uri(inputPath, UriKind.Absolute);
        var decoder = BitmapDecoder.Create(uri, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];

        // Convert to 32-bit BGRA
        var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        int w = bgra.PixelWidth;
        int h = bgra.PixelHeight;

        int stride = w * 4;
        byte[] pixels = new byte[stride * h];
        bgra.CopyPixels(pixels, stride, 0);

        Console.WriteLine($"Isolating foreground ({w}x{h})...");
        RemoveWhiteBackground(pixels, w, h, stride);

        var transparentSource = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, pixels, stride);

        // Save 512x512 master PNG
        var master512 = RenderResized(transparentSource, 512, 512);
        SavePng(master512, outputPng);
        Console.WriteLine($"Saved master PNG: {outputPng}");

        // Build multi-res ICO
        int[] sizes = [256, 128, 64, 48, 32, 24, 16];
        var iconEntries = new List<(int Width, int Height, byte[] Bytes)>();

        foreach (int size in sizes)
        {
            var resized = RenderResized(transparentSource, size, size);
            byte[] pngBytes = EncodePng(resized);
            iconEntries.Add((size, size, pngBytes));
            Console.WriteLine($"Generated {size}x{size} icon frame ({pngBytes.Length} bytes)");
        }

        SaveIco(iconEntries, outputIco);
        Console.WriteLine($"Saved multi-resolution ICO: {outputIco}");
    }

    private static void RemoveWhiteBackground(byte[] pixels, int w, int h, int stride)
    {
        bool[] visited = new bool[w * h];
        var queue = new Queue<int>();

        void EnqueueIfValid(int x, int y)
        {
            if (x >= 0 && x < w && y >= 0 && y < h)
            {
                int idx = y * w + x;
                if (!visited[idx])
                {
                    visited[idx] = true;
                    queue.Enqueue(idx);
                }
            }
        }

        // Enqueue border perimeter
        for (int x = 0; x < w; x++)
        {
            EnqueueIfValid(x, 0);
            EnqueueIfValid(x, h - 1);
        }
        for (int y = 0; y < h; y++)
        {
            EnqueueIfValid(0, y);
            EnqueueIfValid(w - 1, y);
        }

        while (queue.Count > 0)
        {
            int idx = queue.Dequeue();
            int px = idx % w;
            int py = idx / w;
            int offset = py * stride + px * 4;

            byte b = pixels[offset];
            byte g = pixels[offset + 1];
            byte r = pixels[offset + 2];

            // Background white threshold
            if (r >= 232 && g >= 232 && b >= 232)
            {
                pixels[offset + 3] = 0; // Transparent
                EnqueueIfValid(px + 1, py);
                EnqueueIfValid(px - 1, py);
                EnqueueIfValid(px, py + 1);
                EnqueueIfValid(px, py - 1);
            }
        }

        // Feather boundary pixels to avoid harsh edges
        for (int py = 1; py < h - 1; py++)
        {
            for (int px = 1; px < w - 1; px++)
            {
                int offset = py * stride + px * 4;
                if (pixels[offset + 3] != 0)
                {
                    int transNeighbors = 0;
                    if (pixels[py * stride + (px + 1) * 4 + 3] == 0) transNeighbors++;
                    if (pixels[py * stride + (px - 1) * 4 + 3] == 0) transNeighbors++;
                    if (pixels[(py + 1) * stride + px * 4 + 3] == 0) transNeighbors++;
                    if (pixels[(py - 1) * stride + px * 4 + 3] == 0) transNeighbors++;

                    if (transNeighbors > 0)
                    {
                        byte b = pixels[offset];
                        byte g = pixels[offset + 1];
                        byte r = pixels[offset + 2];
                        if (r >= 215 && g >= 215 && b >= 215)
                        {
                            double factor = (4.0 - transNeighbors) / 4.0;
                            pixels[offset + 3] = (byte)(255 * factor * 0.85);
                        }
                    }
                }
            }
        }
    }

    private static BitmapSource RenderResized(BitmapSource source, int targetWidth, int targetHeight)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(source, new Rect(0, 0, targetWidth, targetHeight));
        }

        var rtb = new RenderTargetBitmap(targetWidth, targetHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private static void SavePng(BitmapSource source, string path)
    {
        byte[] bytes = EncodePng(source);
        File.WriteAllBytes(path, bytes);
    }

    private static void SaveIco(List<(int Width, int Height, byte[] Bytes)> entries, string path)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // Header: ICONDIR
        writer.Write((ushort)0); // Reserved
        writer.Write((ushort)1); // Type 1 = Icon
        writer.Write((ushort)entries.Count);

        int dataOffset = 6 + 16 * entries.Count;

        foreach (var entry in entries)
        {
            byte w = entry.Width >= 256 ? (byte)0 : (byte)entry.Width;
            byte h = entry.Height >= 256 ? (byte)0 : (byte)entry.Height;
            writer.Write(w);
            writer.Write(h);
            writer.Write((byte)0); // ColorCount
            writer.Write((byte)0); // Reserved
            writer.Write((ushort)1); // Planes
            writer.Write((ushort)32); // BitCount
            writer.Write((uint)entry.Bytes.Length);
            writer.Write((uint)dataOffset);

            dataOffset += entry.Bytes.Length;
        }

        foreach (var entry in entries)
        {
            writer.Write(entry.Bytes);
        }

        File.WriteAllBytes(path, ms.ToArray());
    }
}
