using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace GalgameUiTranslator
{
    public enum DdsFormat
    {
        Bgra32,
        Bc1,
        Bc2,
        Bc3
    }

    public sealed class DdsInfo
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int MipMapCount { get; set; } = 1;
        public DdsFormat Format { get; set; }
        public bool HasAlpha { get; set; }
        public bool UsesDx10Header { get; set; }
        public int DxgiFormat { get; set; }
        public int DataOffset { get; set; } = 128;
        public int PitchOrLinearSize { get; set; }
        public uint RedMask { get; set; }
        public uint GreenMask { get; set; }
        public uint BlueMask { get; set; }
        public uint AlphaMask { get; set; }

        public string FormatName => Format == DdsFormat.Bc1 ? "DXT1 / BC1"
            : Format == DdsFormat.Bc2 ? "DXT3 / BC2"
            : Format == DdsFormat.Bc3 ? "DXT5 / BC3"
            : "32-bit BGRA";
    }

    public static class DdsCodec
    {
        private const uint DdsMagic = 0x20534444;
        private const uint FourCcDxt1 = 0x31545844;
        private const uint FourCcDxt3 = 0x33545844;
        private const uint FourCcDxt5 = 0x35545844;
        private const uint FourCcDx10 = 0x30315844;

        public static DdsInfo ReadInfo(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new BinaryReader(stream, Encoding.ASCII, false))
                return ReadInfo(reader);
        }

        public static Bitmap Load(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new BinaryReader(stream, Encoding.ASCII, false))
            {
                var info = ReadInfo(reader);
                stream.Position = info.DataOffset;
                var pixels = info.Format == DdsFormat.Bgra32
                    ? DecodeUncompressed(reader, info)
                    : DecodeBlocks(reader, info);
                return CreateBitmap(info.Width, info.Height, pixels);
            }
        }

        public static void Save(Bitmap bitmap, string path, DdsInfo template)
        {
            if (bitmap == null) throw new ArgumentNullException(nameof(bitmap));
            if (template == null) throw new ArgumentNullException(nameof(template));
            if (bitmap.Width != template.Width || bitmap.Height != template.Height)
                throw new InvalidOperationException("DDS 导出尺寸必须与源文件完全一致。");

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var mipCount = CalculateMipCount(bitmap.Width, bitmap.Height, template.MipMapCount);
            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream, Encoding.ASCII, false))
            {
                WriteHeader(writer, template, mipCount);
                Bitmap current = bitmap;
                var ownsCurrent = false;
                try
                {
                    for (var level = 0; level < mipCount; level++)
                    {
                        var pixels = ReadBitmapPixels(current);
                        if (template.Format == DdsFormat.Bgra32)
                            EncodeUncompressed(writer, pixels, current.Width, current.Height, template);
                        else
                            EncodeBlocks(writer, pixels, current.Width, current.Height, template.Format);

                        if (level + 1 < mipCount)
                        {
                            var next = CreateMipMap(current);
                            if (ownsCurrent) current.Dispose();
                            current = next;
                            ownsCurrent = true;
                        }
                    }
                }
                finally
                {
                    if (ownsCurrent) current.Dispose();
                }
            }
        }

        private static DdsInfo ReadInfo(BinaryReader reader)
        {
            if (reader.BaseStream.Length < 128 || reader.ReadUInt32() != DdsMagic)
                throw new InvalidDataException("文件不是有效的 DDS 纹理。");
            if (reader.ReadUInt32() != 124)
                throw new InvalidDataException("DDS 头长度不正确。");

            reader.ReadUInt32();
            var height = checked((int)reader.ReadUInt32());
            var width = checked((int)reader.ReadUInt32());
            var pitch = checked((int)reader.ReadUInt32());
            reader.ReadUInt32();
            var mipCount = checked((int)reader.ReadUInt32());
            for (var index = 0; index < 11; index++) reader.ReadUInt32();
            if (reader.ReadUInt32() != 32)
                throw new InvalidDataException("DDS 像素格式头长度不正确。");
            var pixelFlags = reader.ReadUInt32();
            var fourCc = reader.ReadUInt32();
            var rgbBits = reader.ReadUInt32();
            var redMask = reader.ReadUInt32();
            var greenMask = reader.ReadUInt32();
            var blueMask = reader.ReadUInt32();
            var alphaMask = reader.ReadUInt32();
            reader.ReadUInt32();
            var caps2 = reader.ReadUInt32();
            reader.ReadUInt32();
            reader.ReadUInt32();
            reader.ReadUInt32();

            if (width <= 0 || height <= 0)
                throw new InvalidDataException("DDS 尺寸无效。");
            if ((caps2 & 0x0000FE00) != 0)
                throw new NotSupportedException("暂不支持立方体或体积 DDS 纹理。");

            var info = new DdsInfo
            {
                Width = width,
                Height = height,
                MipMapCount = Math.Max(1, mipCount),
                PitchOrLinearSize = pitch,
                RedMask = redMask,
                GreenMask = greenMask,
                BlueMask = blueMask,
                AlphaMask = alphaMask,
                HasAlpha = alphaMask != 0
            };

            if ((pixelFlags & 0x4) != 0)
            {
                if (fourCc == FourCcDxt1)
                {
                    info.Format = DdsFormat.Bc1;
                    info.HasAlpha = true;
                }
                else if (fourCc == FourCcDxt3)
                {
                    info.Format = DdsFormat.Bc2;
                    info.HasAlpha = true;
                }
                else if (fourCc == FourCcDxt5)
                {
                    info.Format = DdsFormat.Bc3;
                    info.HasAlpha = true;
                }
                else if (fourCc == FourCcDx10)
                {
                    if (reader.BaseStream.Length < 148)
                        throw new InvalidDataException("DDS DX10 扩展头不完整。");
                    info.UsesDx10Header = true;
                    info.DataOffset = 148;
                    info.DxgiFormat = reader.ReadInt32();
                    var resourceDimension = reader.ReadInt32();
                    reader.ReadUInt32();
                    var arraySize = reader.ReadUInt32();
                    reader.ReadUInt32();
                    if (resourceDimension != 3 || arraySize != 1)
                        throw new NotSupportedException("只支持单张二维 DDS DX10 纹理。");
                    info.Format = GetDx10Format(info.DxgiFormat);
                    info.HasAlpha = true;
                }
                else
                {
                    throw new NotSupportedException(
                        "暂不支持 DDS FourCC 格式：" + FourCcToString(fourCc) + "。支持 DXT1、DXT3、DXT5。 ");
                }
            }
            else if ((pixelFlags & 0x40) != 0 && rgbBits == 32)
            {
                info.Format = DdsFormat.Bgra32;
                if (info.RedMask == 0 && info.GreenMask == 0 && info.BlueMask == 0)
                {
                    info.RedMask = 0x00FF0000;
                    info.GreenMask = 0x0000FF00;
                    info.BlueMask = 0x000000FF;
                    info.AlphaMask = 0xFF000000;
                    info.HasAlpha = true;
                }
            }
            else
            {
                throw new NotSupportedException("暂不支持该 DDS 像素格式；当前支持 BC1、BC2、BC3 和 32 位 BGRA。 ");
            }

            return info;
        }

        private static DdsFormat GetDx10Format(int dxgiFormat)
        {
            if (dxgiFormat == 71 || dxgiFormat == 72) return DdsFormat.Bc1;
            if (dxgiFormat == 74 || dxgiFormat == 75) return DdsFormat.Bc2;
            if (dxgiFormat == 77 || dxgiFormat == 78) return DdsFormat.Bc3;
            if (dxgiFormat == 98 || dxgiFormat == 99)
                throw new NotSupportedException("暂不支持 BC7 DDS；请先转换为 BC3/DXT5 后处理。");
            throw new NotSupportedException("暂不支持 DDS DXGI 格式编号：" + dxgiFormat + "。 ");
        }

        private static int[] DecodeBlocks(BinaryReader reader, DdsInfo info)
        {
            var output = new int[info.Width * info.Height];
            var blocksWide = (info.Width + 3) / 4;
            var blocksHigh = (info.Height + 3) / 4;
            for (var blockY = 0; blockY < blocksHigh; blockY++)
            {
                for (var blockX = 0; blockX < blocksWide; blockX++)
                {
                    var alpha = new byte[16];
                    if (info.Format == DdsFormat.Bc2) DecodeBc2Alpha(reader, alpha);
                    else if (info.Format == DdsFormat.Bc3) DecodeBc3Alpha(reader, alpha);
                    else for (var index = 0; index < alpha.Length; index++) alpha[index] = 255;

                    var color0 = reader.ReadUInt16();
                    var color1 = reader.ReadUInt16();
                    var indices = reader.ReadUInt32();
                    var forceFour = info.Format != DdsFormat.Bc1;
                    var palette = BuildColorPalette(color0, color1, forceFour);
                    for (var pixel = 0; pixel < 16; pixel++)
                    {
                        var x = blockX * 4 + pixel % 4;
                        var y = blockY * 4 + pixel / 4;
                        if (x >= info.Width || y >= info.Height) continue;
                        var paletteIndex = (int)((indices >> (pixel * 2)) & 0x3);
                        var color = palette[paletteIndex];
                        var pixelAlpha = info.Format == DdsFormat.Bc1 ? color.A : alpha[pixel];
                        output[y * info.Width + x] = Color.FromArgb(
                            pixelAlpha, color.R, color.G, color.B).ToArgb();
                    }
                }
            }
            return output;
        }

        private static int[] DecodeUncompressed(BinaryReader reader, DdsInfo info)
        {
            var output = new int[info.Width * info.Height];
            var pitch = Math.Max(info.Width * 4, info.PitchOrLinearSize);
            for (var y = 0; y < info.Height; y++)
            {
                for (var x = 0; x < info.Width; x++)
                {
                    var value = reader.ReadUInt32();
                    output[y * info.Width + x] = Color.FromArgb(
                        info.AlphaMask == 0 ? 255 : ReadMaskedChannel(value, info.AlphaMask),
                        ReadMaskedChannel(value, info.RedMask),
                        ReadMaskedChannel(value, info.GreenMask),
                        ReadMaskedChannel(value, info.BlueMask)).ToArgb();
                }
                var padding = pitch - info.Width * 4;
                if (padding > 0) reader.ReadBytes(padding);
            }
            return output;
        }

        private static void DecodeBc2Alpha(BinaryReader reader, byte[] alpha)
        {
            var values = reader.ReadUInt64();
            for (var index = 0; index < 16; index++)
                alpha[index] = (byte)(((values >> (index * 4)) & 0xF) * 17);
        }

        private static void DecodeBc3Alpha(BinaryReader reader, byte[] alpha)
        {
            var first = reader.ReadByte();
            var second = reader.ReadByte();
            ulong indices = 0;
            for (var index = 0; index < 6; index++) indices |= (ulong)reader.ReadByte() << (index * 8);
            var palette = BuildAlphaPalette(first, second);
            for (var index = 0; index < 16; index++)
                alpha[index] = palette[(indices >> (index * 3)) & 0x7];
        }

        private static void EncodeBlocks(BinaryWriter writer, int[] pixels, int width, int height, DdsFormat format)
        {
            var blocksWide = (width + 3) / 4;
            var blocksHigh = (height + 3) / 4;
            var block = new Color[16];
            for (var blockY = 0; blockY < blocksHigh; blockY++)
            {
                for (var blockX = 0; blockX < blocksWide; blockX++)
                {
                    ReadBlock(pixels, width, height, blockX, blockY, block);
                    if (format == DdsFormat.Bc2) EncodeBc2Alpha(writer, block);
                    else if (format == DdsFormat.Bc3) EncodeBc3Alpha(writer, block);
                    EncodeColorBlock(writer, block, format != DdsFormat.Bc1);
                }
            }
        }

        private static void EncodeUncompressed(
            BinaryWriter writer,
            int[] pixels,
            int width,
            int height,
            DdsInfo info)
        {
            var redMask = info.RedMask == 0 ? 0x00FF0000u : info.RedMask;
            var greenMask = info.GreenMask == 0 ? 0x0000FF00u : info.GreenMask;
            var blueMask = info.BlueMask == 0 ? 0x000000FFu : info.BlueMask;
            var alphaMask = info.AlphaMask;
            for (var index = 0; index < width * height; index++)
            {
                var color = Color.FromArgb(pixels[index]);
                var value = PackMaskedChannel(color.R, redMask) |
                            PackMaskedChannel(color.G, greenMask) |
                            PackMaskedChannel(color.B, blueMask);
                if (alphaMask != 0) value |= PackMaskedChannel(color.A, alphaMask);
                writer.Write(value);
            }
        }

        private static void EncodeColorBlock(BinaryWriter writer, Color[] block, bool forceFourColor)
        {
            var opaque = new List<Color>();
            var hasTransparent = false;
            foreach (var color in block)
            {
                if (color.A < 128) hasTransparent = true;
                else opaque.Add(color);
            }
            if (opaque.Count == 0) opaque.Add(Color.Black);

            var minimum = opaque[0];
            var maximum = opaque[0];
            var minimumLuminance = Luminance(minimum);
            var maximumLuminance = minimumLuminance;
            foreach (var color in opaque)
            {
                var luminance = Luminance(color);
                if (luminance < minimumLuminance) { minimum = color; minimumLuminance = luminance; }
                if (luminance > maximumLuminance) { maximum = color; maximumLuminance = luminance; }
            }

            var color0 = ToRgb565(maximum);
            var color1 = ToRgb565(minimum);
            var transparentMode = hasTransparent && !forceFourColor;
            if (transparentMode)
            {
                if (color0 > color1) Swap(ref color0, ref color1);
            }
            else
            {
                if (color0 <= color1) Swap(ref color0, ref color1);
                if (color0 == color1)
                {
                    if (color0 < ushort.MaxValue) color0++;
                    else color1--;
                }
            }

            var palette = BuildColorPalette(color0, color1, forceFourColor);
            uint indices = 0;
            for (var index = 0; index < 16; index++)
            {
                var selected = transparentMode && block[index].A < 128
                    ? 3
                    : FindNearestColor(block[index], palette, transparentMode ? 3 : 4);
                indices |= (uint)selected << (index * 2);
            }
            writer.Write(color0);
            writer.Write(color1);
            writer.Write(indices);
        }

        private static void EncodeBc2Alpha(BinaryWriter writer, Color[] block)
        {
            ulong values = 0;
            for (var index = 0; index < 16; index++)
                values |= (ulong)(block[index].A / 17) << (index * 4);
            writer.Write(values);
        }

        private static void EncodeBc3Alpha(BinaryWriter writer, Color[] block)
        {
            byte minimum = 255;
            byte maximum = 0;
            foreach (var color in block)
            {
                minimum = Math.Min(minimum, color.A);
                maximum = Math.Max(maximum, color.A);
            }
            if (maximum == minimum)
            {
                if (maximum > 0) minimum--;
                else maximum++;
            }
            writer.Write(maximum);
            writer.Write(minimum);
            var palette = BuildAlphaPalette(maximum, minimum);
            ulong indices = 0;
            for (var index = 0; index < 16; index++)
            {
                var nearest = 0;
                var distance = int.MaxValue;
                for (var paletteIndex = 0; paletteIndex < 8; paletteIndex++)
                {
                    var candidate = Math.Abs(block[index].A - palette[paletteIndex]);
                    if (candidate < distance) { distance = candidate; nearest = paletteIndex; }
                }
                indices |= (ulong)nearest << (index * 3);
            }
            for (var index = 0; index < 6; index++) writer.Write((byte)(indices >> (index * 8)));
        }

        private static Color[] BuildColorPalette(ushort first, ushort second, bool forceFourColor)
        {
            var firstColor = FromRgb565(first);
            var secondColor = FromRgb565(second);
            var palette = new Color[4];
            palette[0] = firstColor;
            palette[1] = secondColor;
            if (first > second || forceFourColor)
            {
                palette[2] = Interpolate(firstColor, secondColor, 2, 1, 3);
                palette[3] = Interpolate(firstColor, secondColor, 1, 2, 3);
            }
            else
            {
                palette[2] = Interpolate(firstColor, secondColor, 1, 1, 2);
                palette[3] = Color.Transparent;
            }
            return palette;
        }

        private static byte[] BuildAlphaPalette(byte first, byte second)
        {
            var palette = new byte[8];
            palette[0] = first;
            palette[1] = second;
            if (first > second)
            {
                for (var index = 1; index <= 6; index++)
                    palette[index + 1] = (byte)(((7 - index) * first + index * second) / 7);
            }
            else
            {
                for (var index = 1; index <= 4; index++)
                    palette[index + 1] = (byte)(((5 - index) * first + index * second) / 5);
                palette[6] = 0;
                palette[7] = 255;
            }
            return palette;
        }

        private static void ReadBlock(
            int[] pixels,
            int width,
            int height,
            int blockX,
            int blockY,
            Color[] block)
        {
            for (var pixel = 0; pixel < 16; pixel++)
            {
                var x = Math.Min(width - 1, blockX * 4 + pixel % 4);
                var y = Math.Min(height - 1, blockY * 4 + pixel / 4);
                block[pixel] = Color.FromArgb(pixels[y * width + x]);
            }
        }

        private static void WriteHeader(BinaryWriter writer, DdsInfo template, int mipCount)
        {
            var compressed = template.Format != DdsFormat.Bgra32;
            var blockSize = template.Format == DdsFormat.Bc1 ? 8 : 16;
            var pitchOrLinear = compressed
                ? Math.Max(1, (template.Width + 3) / 4) *
                  Math.Max(1, (template.Height + 3) / 4) * blockSize
                : template.Width * 4;
            var flags = 0x1u | 0x2u | 0x4u | 0x1000u | (compressed ? 0x80000u : 0x8u);
            if (mipCount > 1) flags |= 0x20000u;

            writer.Write(DdsMagic);
            writer.Write(124u);
            writer.Write(flags);
            writer.Write((uint)template.Height);
            writer.Write((uint)template.Width);
            writer.Write((uint)pitchOrLinear);
            writer.Write(0u);
            writer.Write((uint)mipCount);
            for (var index = 0; index < 11; index++) writer.Write(0u);
            writer.Write(32u);
            if (compressed)
            {
                writer.Write(0x4u);
                writer.Write(template.UsesDx10Header ? FourCcDx10
                    : template.Format == DdsFormat.Bc1 ? FourCcDxt1
                    : template.Format == DdsFormat.Bc2 ? FourCcDxt3
                    : FourCcDxt5);
                writer.Write(0u);
                writer.Write(0u);
                writer.Write(0u);
                writer.Write(0u);
                writer.Write(0u);
            }
            else
            {
                writer.Write(template.AlphaMask == 0 ? 0x40u : 0x41u);
                writer.Write(0u);
                writer.Write(32u);
                writer.Write(template.RedMask == 0 ? 0x00FF0000u : template.RedMask);
                writer.Write(template.GreenMask == 0 ? 0x0000FF00u : template.GreenMask);
                writer.Write(template.BlueMask == 0 ? 0x000000FFu : template.BlueMask);
                writer.Write(template.AlphaMask);
            }

            writer.Write(mipCount > 1 ? 0x401008u : 0x1000u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            writer.Write(0u);
            if (template.UsesDx10Header)
            {
                writer.Write(template.DxgiFormat);
                writer.Write(3);
                writer.Write(0u);
                writer.Write(1u);
                writer.Write(0u);
            }
        }

        private static Bitmap CreateBitmap(int width, int height, int[] pixels)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            var data = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.WriteOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                if (data.Stride == width * 4)
                {
                    Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
                }
                else
                {
                    for (var y = 0; y < height; y++)
                        Marshal.Copy(pixels, y * width, data.Scan0 + y * data.Stride, width);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            return bitmap;
        }

        private static int[] ReadBitmapPixels(Bitmap bitmap)
        {
            using (var converted = bitmap.Clone(
                       new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                       PixelFormat.Format32bppArgb))
            {
                var pixels = new int[bitmap.Width * bitmap.Height];
                var data = converted.LockBits(
                    new Rectangle(0, 0, converted.Width, converted.Height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);
                try
                {
                    if (data.Stride == bitmap.Width * 4)
                    {
                        Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                    }
                    else
                    {
                        for (var y = 0; y < bitmap.Height; y++)
                            Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * bitmap.Width, bitmap.Width);
                    }
                }
                finally
                {
                    converted.UnlockBits(data);
                }
                return pixels;
            }
        }

        private static Bitmap CreateMipMap(Bitmap source)
        {
            var width = Math.Max(1, source.Width / 2);
            var height = Math.Max(1, source.Height / 2);
            var output = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(output))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, width, height));
            }
            return output;
        }

        private static int CalculateMipCount(int width, int height, int requested)
        {
            var maximum = 1;
            while (width > 1 || height > 1)
            {
                width = Math.Max(1, width / 2);
                height = Math.Max(1, height / 2);
                maximum++;
            }
            return Math.Max(1, Math.Min(maximum, requested <= 0 ? 1 : requested));
        }

        private static int FindNearestColor(Color color, Color[] palette, int count)
        {
            var nearest = 0;
            var distance = int.MaxValue;
            for (var index = 0; index < count; index++)
            {
                var red = color.R - palette[index].R;
                var green = color.G - palette[index].G;
                var blue = color.B - palette[index].B;
                var candidate = red * red + green * green + blue * blue;
                if (candidate < distance) { distance = candidate; nearest = index; }
            }
            return nearest;
        }

        private static ushort ToRgb565(Color color)
        {
            return (ushort)(((color.R * 31 + 127) / 255 << 11) |
                            ((color.G * 63 + 127) / 255 << 5) |
                            ((color.B * 31 + 127) / 255));
        }

        private static Color FromRgb565(ushort value)
        {
            return Color.FromArgb(
                255,
                ((value >> 11) & 31) * 255 / 31,
                ((value >> 5) & 63) * 255 / 63,
                (value & 31) * 255 / 31);
        }

        private static Color Interpolate(Color first, Color second, int firstWeight, int secondWeight, int divisor)
        {
            return Color.FromArgb(
                (firstWeight * first.A + secondWeight * second.A) / divisor,
                (firstWeight * first.R + secondWeight * second.R) / divisor,
                (firstWeight * first.G + secondWeight * second.G) / divisor,
                (firstWeight * first.B + secondWeight * second.B) / divisor);
        }

        private static int Luminance(Color color)
        {
            return color.R * 299 + color.G * 587 + color.B * 114;
        }

        private static int ReadMaskedChannel(uint value, uint mask)
        {
            if (mask == 0) return 0;
            var shift = TrailingZeroCount(mask);
            var maximum = mask >> shift;
            return (int)(((value & mask) >> shift) * 255 / maximum);
        }

        private static uint PackMaskedChannel(byte channel, uint mask)
        {
            if (mask == 0) return 0;
            var shift = TrailingZeroCount(mask);
            var maximum = mask >> shift;
            return ((uint)channel * maximum / 255 << shift) & mask;
        }

        private static int TrailingZeroCount(uint value)
        {
            var count = 0;
            while ((value & 1) == 0) { value >>= 1; count++; }
            return count;
        }

        private static string FourCcToString(uint value)
        {
            return new string(new[]
            {
                (char)(value & 0xFF),
                (char)((value >> 8) & 0xFF),
                (char)((value >> 16) & 0xFF),
                (char)((value >> 24) & 0xFF)
            });
        }

        private static void Swap(ref ushort first, ref ushort second)
        {
            var temporary = first;
            first = second;
            second = temporary;
        }
    }
}
