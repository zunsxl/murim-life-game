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

            return Pixels[((long)y * Width + x) * 4L + 3L];
        }

        public void GetRgb(int x, int y, out byte r, out byte g, out byte b)
        {
            long c = Channels;
            long idx = ((long)y * Width + x) * c;
            r = Pixels[idx];
            g = Pixels[idx + 1L];
            b = Pixels[idx + 2L];
        }
    }

    public readonly struct PngContract
    {
        public int ExpectedWidth { get; }
        public int ExpectedHeight { get; }
        public bool RequiresAlpha { get; }
        public bool EnforceContract { get; }

        public PngContract(int expectedWidth, int expectedHeight, bool requiresAlpha)
        {
            ExpectedWidth = expectedWidth;
            ExpectedHeight = expectedHeight;
            RequiresAlpha = requiresAlpha;
            EnforceContract = true;
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
        public const int MaxDimension = 8192;
        private static readonly byte[] PngSignature = { 137, 80, 78, 71, 13, 10, 26, 10 };

        public static PngReadResult ReadFile(string path, bool decodePixels)
        {
            return ReadFile(path, default, decodePixels);
        }

        public static PngReadResult ReadFile(string path, PngContract contract, bool decodePixels)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return new PngReadResult(false, "File does not exist.", default);
                }

                byte[] data = File.ReadAllBytes(path);
                return ReadBytes(data, contract, decodePixels);
            }
            catch (IOException)
            {
                return new PngReadResult(false, "Unable to read PNG file.", default);
            }
            catch (UnauthorizedAccessException)
            {
                return new PngReadResult(false, "Unable to access PNG file.", default);
            }
        }

        public static PngReadResult ReadBytes(byte[] data, bool decodePixels)
        {
            return ReadBytes(data, default, decodePixels);
        }

        public static PngReadResult ReadBytes(byte[] data, PngContract contract, bool decodePixels)
        {
            if (!HasValidSignature(data))
            {
                return new PngReadResult(false, "Invalid PNG signature.", default);
            }

            try
            {
                return ParseChunks(data, contract, decodePixels);
            }
            catch (InvalidDataException)
            {
                return new PngReadResult(false, "Corrupt PNG data.", default);
            }
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
            public readonly int Compression;
            public readonly int Filter;
            public readonly int Interlace;

            public IhdrInfo(byte[] data, long payloadPos)
            {
                uint w = ReadUInt32BE(data, payloadPos);
                uint h = ReadUInt32BE(data, payloadPos + 4L);
                Width = w <= int.MaxValue ? (int)w : -1;
                Height = h <= int.MaxValue ? (int)h : -1;
                BitDepth = data[payloadPos + 8L];
                ColorType = data[payloadPos + 9L];
                Compression = data[payloadPos + 10L];
                Filter = data[payloadPos + 11L];
                Interlace = data[payloadPos + 12L];
            }
        }

        private sealed class ChunkParseState : IDisposable
        {
            public readonly PngContract Contract;
            public readonly bool DecodePixels;
            public bool IhdrSeen;
            public bool IendSeen;
            public bool HasIdat;
            public IhdrInfo Ihdr;
            public string Error;
            public readonly MemoryStream Idat = new MemoryStream();

            public ChunkParseState(PngContract contract, bool decodePixels)
            {
                Contract = contract;
                DecodePixels = decodePixels;
            }

            public void Dispose()
            {
                Idat.Dispose();
            }
        }

        private static PngReadResult ParseChunks(byte[] data, PngContract contract, bool decodePixels)
        {
            long pos = 8L;
            using var state = new ChunkParseState(contract, decodePixels);

            while (pos + 12L <= data.Length)
            {
                uint lengthU = ReadUInt32BE(data, pos);
                if (lengthU > int.MaxValue || pos + 12L + lengthU > data.Length)
                {
                    return new PngReadResult(false, "Truncated PNG chunk.", default);
                }

                int length = (int)lengthU;
                string type = ReadChunkType(data, pos + 4L);
                long payloadPos = pos + 8L;
                pos = payloadPos + length + 4L;

                if (!TryHandleChunk(data, type, payloadPos, length, state))
                {
                    return new PngReadResult(false, state.Error, default);
                }

                if (state.IendSeen)
                {
                    break;
                }
            }

            if (!state.IhdrSeen || !state.IendSeen || !state.HasIdat)
            {
                return new PngReadResult(false, "Missing IHDR, IDAT, or IEND chunk.", default);
            }

            if (!decodePixels)
            {
                var info = new ArtPixelBuffer(
                    state.Ihdr.Width,
                    state.Ihdr.Height,
                    state.Ihdr.BitDepth,
                    state.Ihdr.ColorType == 6,
                    null);
                return new PngReadResult(true, null, info);
            }

            return DecodePixelBuffer(state.Idat.ToArray(), state.Ihdr);
        }

        private static bool TryHandleChunk(
            byte[] data,
            string type,
            long payloadPos,
            int length,
            ChunkParseState state)
        {
            if (!state.IhdrSeen)
            {
                if (type != "IHDR")
                {
                    state.Error = "IHDR must be the first PNG chunk.";
                    return false;
                }

                if (!TryParseAndValidateIhdr(data, payloadPos, length, state))
                {
                    return false;
                }

                state.IhdrSeen = true;
                return true;
            }

            if (type == "IHDR")
            {
                state.Error = "Duplicate IHDR chunk.";
                return false;
            }

            if (type == "IDAT")
            {
                state.HasIdat = true;
                if (state.DecodePixels)
                {
                    state.Idat.Write(data, (int)payloadPos, length);
                }
            }
            else if (type == "IEND")
            {
                state.IendSeen = true;
            }

            return true;
        }

        private static bool TryParseAndValidateIhdr(
            byte[] data,
            long payloadPos,
            int length,
            ChunkParseState state)
        {
            if (length != 13)
            {
                state.Error = "Invalid IHDR length.";
                return false;
            }

            var ihdr = new IhdrInfo(data, payloadPos);
            if (ihdr.Width <= 0 || ihdr.Height <= 0 || ihdr.Width > MaxDimension || ihdr.Height > MaxDimension)
            {
                state.Error = $"PNG dimensions {ihdr.Width}x{ihdr.Height} outside allowed range [1..{MaxDimension}].";
                return false;
            }

            if (ihdr.Compression != 0 || ihdr.Filter != 0)
            {
                state.Error = "Unsupported PNG compression or filter method.";
                return false;
            }

            if (ihdr.BitDepth != 8 || ihdr.Interlace != 0)
            {
                state.Error = $"Unsupported PNG IHDR format (bitDepth={ihdr.BitDepth}, interlace={ihdr.Interlace}; expected 8-bit non-interlaced).";
                return false;
            }

            if (!ValidateIhdrContract(ihdr, state.Contract, out string err))
            {
                state.Error = err;
                return false;
            }

            state.Ihdr = ihdr;
            return true;
        }

        private static bool ValidateIhdrContract(IhdrInfo ihdr, PngContract contract, out string error)
        {
            error = null;
            if (!contract.EnforceContract)
            {
                if (ihdr.ColorType != 2 && ihdr.ColorType != 6)
                {
                    error = $"Unsupported PNG colour type {ihdr.ColorType} (expected 2 RGB or 6 RGBA).";
                    return false;
                }

                return true;
            }

            int expectedColorType = contract.RequiresAlpha ? 6 : 2;
            if (ihdr.ColorType != expectedColorType)
            {
                error = $"Unsupported PNG colour type {ihdr.ColorType} (expected {expectedColorType}).";
                return false;
            }

            if (ihdr.Width != contract.ExpectedWidth || ihdr.Height != contract.ExpectedHeight)
            {
                error = $"PNG IHDR dimensions {ihdr.Width}x{ihdr.Height} do not match contract {contract.ExpectedWidth}x{contract.ExpectedHeight}.";
                return false;
            }

            return true;
        }

        private static PngReadResult DecodePixelBuffer(byte[] zlibBytes, IhdrInfo ihdr)
        {
            long bpp = ihdr.ColorType == 6 ? 4L : 3L;
            long stride = (long)ihdr.Width * bpp;
            long expectedRawBytes = (long)ihdr.Height * (stride + 1L);

            if (!TryDecompressZlib(zlibBytes, expectedRawBytes, out byte[] raw, out string error))
            {
                return new PngReadResult(false, error, default);
            }

            if (!TryUnfilterBottomUp(raw, ihdr, out byte[] pixels, out error))
            {
                return new PngReadResult(false, error, default);
            }

            return new PngReadResult(
                true,
                null,
                new ArtPixelBuffer(ihdr.Width, ihdr.Height, ihdr.BitDepth, ihdr.ColorType == 6, pixels));
        }

        private static bool TryDecompressZlib(
            byte[] zlibBytes,
            long expectedRawBytes,
            out byte[] raw,
            out string error)
        {
            raw = null;
            error = null;
            if (zlibBytes == null || zlibBytes.Length < 6)
            {
                error = "Invalid zlib stream.";
                return false;
            }

            if ((zlibBytes[0] & 0x0F) != 8 || (((zlibBytes[0] << 8) | zlibBytes[1]) % 31) != 0)
            {
                error = "Invalid zlib header.";
                return false;
            }

            int headerLen = (zlibBytes[1] & 0x20) != 0 ? 6 : 2;
            if (zlibBytes.Length - headerLen - 4 <= 0)
            {
                error = "Empty deflate stream.";
                return false;
            }

            return ReadExactDeflateBytes(zlibBytes, headerLen, expectedRawBytes, out raw, out error);
        }

        private static bool ReadExactDeflateBytes(
            byte[] zlibBytes,
            int headerLen,
            long expectedRawBytes,
            out byte[] raw,
            out string error)
        {
            int deflateLen = zlibBytes.Length - headerLen - 4;
            raw = new byte[expectedRawBytes];
            error = null;
            try
            {
                using var input = new MemoryStream(zlibBytes, headerLen, deflateLen);
                using var deflate = new DeflateStream(input, CompressionMode.Decompress);
                long totalRead = 0L;
                while (totalRead < expectedRawBytes)
                {
                    int toRead = (int)Math.Min(65536L, expectedRawBytes - totalRead);
                    int read = deflate.Read(raw, (int)totalRead, toRead);
                    if (read <= 0)
                    {
                        break;
                    }

                    totalRead += read;
                }

                if (totalRead < expectedRawBytes)
                {
                    raw = null;
                    error = "Truncated IDAT pixel payload.";
                    return false;
                }

                byte[] extra = new byte[1];
                if (deflate.Read(extra, 0, 1) > 0)
                {
                    raw = null;
                    error = "Oversized IDAT pixel payload.";
                    return false;
                }

                return true;
            }
            catch (InvalidDataException)
            {
                raw = null;
                error = "Corrupt IDAT deflate stream.";
                return false;
            }
        }

        private static bool TryUnfilterBottomUp(
            byte[] raw,
            IhdrInfo ihdr,
            out byte[] pixels,
            out string error)
        {
            int bpp = ihdr.ColorType == 6 ? 4 : 3;
            long stride = (long)ihdr.Width * bpp;
            pixels = new byte[(long)ihdr.Height * stride];
            byte[] prev = new byte[stride];
            byte[] curr = new byte[stride];
            error = null;

            for (int pngRow = 0; pngRow < ihdr.Height; pngRow++)
            {
                long srcOff = pngRow * (stride + 1L);
                if (raw[srcOff] > 4)
                {
                    pixels = null;
                    error = "Unsupported PNG filter type.";
                    return false;
                }

                UnfilterRow(raw, srcOff, bpp, prev, curr);
                int unityY = ihdr.Height - 1 - pngRow;
                Buffer.BlockCopy(curr, 0, pixels, (int)((long)unityY * stride), (int)stride);
                Buffer.BlockCopy(curr, 0, prev, 0, (int)stride);
            }

            return true;
        }

        private static void UnfilterRow(
            byte[] raw,
            long srcOff,
            int bpp,
            byte[] prev,
            byte[] curr)
        {
            byte filter = raw[srcOff];
            int stride = curr.Length;
            for (int i = 0; i < stride; i++)
            {
                byte x = raw[srcOff + 1L + i];
                byte a = i >= bpp ? curr[i - bpp] : (byte)0;
                byte b = prev[i];
                byte c = i >= bpp ? prev[i - bpp] : (byte)0;
                curr[i] = (byte)((x + PredictFilterByte(filter, a, b, c)) & 0xFF);
            }
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
                default:
                    return Paeth(a, b, c);
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

        private static uint ReadUInt32BE(byte[] data, long offset)
        {
            return ((uint)data[offset] << 24)
                | ((uint)data[offset + 1L] << 16)
                | ((uint)data[offset + 2L] << 8)
                | data[offset + 3L];
        }

        private static string ReadChunkType(byte[] data, long offset)
        {
            return string.Concat(
                (char)data[offset],
                (char)data[offset + 1L],
                (char)data[offset + 2L],
                (char)data[offset + 3L]);
        }
    }
}
