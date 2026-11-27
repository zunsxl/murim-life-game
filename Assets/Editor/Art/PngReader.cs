using System;
using System.IO;
using System.IO.Compression;

namespace Murim.Editor.Art
{
    public readonly struct ArtPixelBuffer
    {
        public int Width { get; }
        public int Height { get; }
        public int BitDepth { get; }
        public bool HasAlpha { get; }
        public byte[] Pixels { get; }

        public ArtPixelBuffer(int width, int height, int bitDepth, bool hasAlpha, byte[] pixels)
        {
            Width = width;
            Height = height;
            BitDepth = bitDepth;
            HasAlpha = hasAlpha;
            Pixels = pixels;
        }

        public int Channels => HasAlpha ? 4 : 3;

        public byte GetAlpha(int x, int y)
        {
            if (!HasAlpha || Pixels == null)
            {
                return 255;
            }

            return Pixels[(y * Width + x) * 4 + 3];
        }

        public void GetRgb(int x, int y, out byte r, out byte g, out byte b)
        {
            int c = Channels;
            int idx = (y * Width + x) * c;
            r = Pixels[idx];
            g = Pixels[idx + 1];
            b = Pixels[idx + 2];
        }
    }

    public readonly struct PngReadResult
    {
        public bool Valid { get; }
        public string Error { get; }
        public ArtPixelBuffer Image { get; }

        public PngReadResult(bool valid, string error, ArtPixelBuffer image)
        {
            Valid = valid;
            Error = error;
            Image = image;
        }
    }

    public static class PngReader
    {
        private static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        public static PngReadResult ReadFile(string path, bool decodePixels)
        {
            if (!File.Exists(path))
            {
                return new PngReadResult(false, "File does not exist.", default);
            }

            byte[] data = File.ReadAllBytes(path);
            return ReadBytes(data, decodePixels);
        }

        public static PngReadResult ReadBytes(byte[] data, bool decodePixels)
        {
            if (!HasValidSignature(data))
            {
                return new PngReadResult(false, "Invalid PNG signature.", default);
            }

            return ParseChunks(data, decodePixels);
        }

        private static bool HasValidSignature(byte[] data)
        {
            if (data == null || data.Length < 8)
            {
                return false;
            }

            for (int i = 0; i < 8; i++)
            {
                if (data[i] != PngSignature[i])
                {
                    return false;
                }
            }

            return true;
        }

        private readonly struct IhdrInfo
        {
            public readonly int Width;
            public readonly int Height;
            public readonly int BitDepth;
            public readonly int ColorType;
            public readonly int Interlace;

            public IhdrInfo(byte[] data, int payloadPos)
            {
                Width = ReadInt32BE(data, payloadPos);
                Height = ReadInt32BE(data, payloadPos + 4);
                BitDepth = data[payloadPos + 8];
                ColorType = data[payloadPos + 9];
                Interlace = data[payloadPos + 12];
            }
        }

        private sealed class ChunkParseState : IDisposable
        {
            public bool IhdrSeen;
            public bool IendSeen;
            public bool HasTrns;
            public IhdrInfo Ihdr;
            public readonly MemoryStream Idat = new MemoryStream();

            public void Dispose()
            {
                Idat.Dispose();
            }
        }

        private static PngReadResult ParseChunks(byte[] data, bool decodePixels)
        {
            int pos = 8;
            using var state = new ChunkParseState();

            while (pos + 12 <= data.Length)
            {
                int length = ReadInt32BE(data, pos);
                if (length < 0 || pos + 12L + length > data.Length)
                {
                    return new PngReadResult(false, "Truncated PNG chunk.", default);
                }

                string type = ReadChunkType(data, pos + 4);
                int payloadPos = pos + 8;
                pos = payloadPos + length + 4;

                if (!HandleChunk(data, type, payloadPos, length, state))
                {
                    return new PngReadResult(false, "Invalid IHDR length.", default);
                }

                if (state.IendSeen)
                {
                    break;
                }
            }

            if (!state.IhdrSeen || !state.IendSeen || state.Idat.Length == 0)
            {
                return new PngReadResult(false, "Missing IHDR, IDAT, or IEND chunk.", default);
            }

            bool hasAlpha = state.Ihdr.ColorType == 6
                || state.Ihdr.ColorType == 4
                || (state.Ihdr.ColorType == 3 && state.HasTrns);
            if (!decodePixels)
            {
                var info = new ArtPixelBuffer(state.Ihdr.Width, state.Ihdr.Height, state.Ihdr.BitDepth, hasAlpha, null);
                return new PngReadResult(true, null, info);
            }

            return DecodePixelBuffer(state.Idat.ToArray(), state.Ihdr);
        }

        private static bool HandleChunk(
            byte[] data,
            string type,
            int payloadPos,
            int length,
            ChunkParseState state)
        {
            if (type == "IHDR")
            {
                if (length != 13)
                {
                    return false;
                }

                state.Ihdr = new IhdrInfo(data, payloadPos);
                state.IhdrSeen = true;
            }
            else if (type == "tRNS")
            {
                state.HasTrns = true;
            }
            else if (type == "IDAT")
            {
                state.Idat.Write(data, payloadPos, length);
            }
            else if (type == "IEND")
            {
                state.IendSeen = true;
            }

            return true;
        }

        private static PngReadResult DecodePixelBuffer(byte[] zlibBytes, IhdrInfo ihdr)
        {
            if (ihdr.Interlace != 0 || ihdr.BitDepth != 8 || (ihdr.ColorType != 2 && ihdr.ColorType != 6))
            {
                return new PngReadResult(false, "Unsupported PNG format (expected non-interlaced 8-bit RGB/RGBA).", default);
            }

            int bpp = ihdr.ColorType == 6 ? 4 : 3;
            byte[] raw = DecompressZlib(zlibBytes);
            int stride = ihdr.Width * bpp;
            if (raw.Length < ihdr.Height * (stride + 1))
            {
                return new PngReadResult(false, "Truncated IDAT pixel payload.", default);
            }

            byte[] pixels = UnfilterBottomUp(raw, ihdr.Width, ihdr.Height, bpp);
            return new PngReadResult(true, null, new ArtPixelBuffer(ihdr.Width, ihdr.Height, ihdr.BitDepth, ihdr.ColorType == 6, pixels));
        }

        private static byte[] DecompressZlib(byte[] zlibBytes)
        {
            if (zlibBytes.Length < 6)
            {
                throw new InvalidDataException("Invalid zlib stream.");
            }

            int headerLen = (zlibBytes[1] & 0x20) != 0 ? 6 : 2;
            int deflateLen = zlibBytes.Length - headerLen - 4;
            if (deflateLen <= 0)
            {
                throw new InvalidDataException("Empty deflate stream.");
            }

            using var input = new MemoryStream(zlibBytes, headerLen, deflateLen);
            using var deflate = new DeflateStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            deflate.CopyTo(output);
            return output.ToArray();
        }

        private static byte[] UnfilterBottomUp(byte[] raw, int width, int height, int bpp)
        {
            int stride = width * bpp;
            byte[] pixels = new byte[height * stride];
            byte[] prev = new byte[stride];
            byte[] curr = new byte[stride];

            for (int pngRow = 0; pngRow < height; pngRow++)
            {
                int srcOff = pngRow * (stride + 1);
                byte filter = raw[srcOff];
                for (int i = 0; i < stride; i++)
                {
                    byte x = raw[srcOff + 1 + i];
                    byte a = i >= bpp ? curr[i - bpp] : (byte)0;
                    byte b = prev[i];
                    byte c = i >= bpp ? prev[i - bpp] : (byte)0;
                    curr[i] = (byte)((x + PredictFilterByte(filter, a, b, c)) & 0xFF);
                }

                int unityY = height - 1 - pngRow;
                Buffer.BlockCopy(curr, 0, pixels, unityY * stride, stride);
                Buffer.BlockCopy(curr, 0, prev, 0, stride);
            }

            return pixels;
        }

        private static int PredictFilterByte(byte filter, byte a, byte b, byte c)
        {
            switch (filter)
            {
                case 0:
                    return 0;
                case 1:
                    return a;
                case 2:
                    return b;
                case 3:
                    return (a + b) >> 1;
                case 4:
                    return Paeth(a, b, c);
                default:
                    throw new InvalidDataException("Unsupported PNG filter type.");
            }
        }

        private static int Paeth(int a, int b, int c)
        {
            int p = a + b - c;
            int pa = Math.Abs(p - a);
            int pb = Math.Abs(p - b);
            int pc = Math.Abs(p - c);
            if (pa <= pb && pa <= pc)
            {
                return a;
            }

            return pb <= pc ? b : c;
        }

        private static int ReadInt32BE(byte[] data, int offset)
        {
            return (data[offset] << 24)
                | (data[offset + 1] << 16)
                | (data[offset + 2] << 8)
                | data[offset + 3];
        }

        private static string ReadChunkType(byte[] data, int offset)
        {
            return string.Concat(
                (char)data[offset],
                (char)data[offset + 1],
                (char)data[offset + 2],
                (char)data[offset + 3]);
        }
    }
}
