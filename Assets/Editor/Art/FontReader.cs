using System;
using System.Collections.Generic;
using System.IO;

namespace Murim.Editor.Art
{
    public readonly struct CodepointRange
    {
        public int Start { get; }
        public int End { get; }

        public CodepointRange(int start, int end)
        {
            Start = start;
            End = end;
        }
    }

    public readonly struct FontSnapshot
    {
        public bool Valid { get; }
        public string Error { get; }
        public bool IsVariable { get; }
        public IReadOnlyList<CodepointRange> Ranges { get; }

        public FontSnapshot(bool valid, string error, bool isVariable, IReadOnlyList<CodepointRange> ranges)
        {
            Valid = valid;
            Error = error;
            IsVariable = isVariable;
            Ranges = ranges;
        }

        public bool CoversRange(int minCp, int maxCp)
        {
            if (Ranges == null || Ranges.Count == 0)
            {
                return false;
            }

            for (int cp = minCp; cp <= maxCp; cp++)
            {
                if (!ContainsCodepoint(cp))
                {
                    return false;
                }
            }

            return true;
        }

        private bool ContainsCodepoint(int cp)
        {
            for (int i = 0; i < Ranges.Count; i++)
            {
                CodepointRange r = Ranges[i];
                if (cp >= r.Start && cp <= r.End)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public static class FontReader
    {
        public static FontSnapshot ReadFile(string path)
        {
            if (!File.Exists(path))
            {
                return new FontSnapshot(false, "Font file does not exist.", false, Array.Empty<CodepointRange>());
            }

            return ReadBytes(File.ReadAllBytes(path));
        }

        public static FontSnapshot ReadBytes(byte[] data)
        {
            if (data == null || data.Length < 12)
            {
                return new FontSnapshot(false, "File too small to be a valid SFNT font.", false, Array.Empty<CodepointRange>());
            }

            uint sfnt = ReadUInt32BE(data, 0);
            if (sfnt != 0x00010000u && sfnt != 0x4F54544Fu && sfnt != 0x74727565u)
            {
                return new FontSnapshot(false, "Invalid TTF/OTF SFNT header.", false, Array.Empty<CodepointRange>());
            }

            int numTables = ReadUInt16BE(data, 4);
            if (data.Length < 12 + numTables * 16)
            {
                return new FontSnapshot(false, "Truncated SFNT table directory.", false, Array.Empty<CodepointRange>());
            }

            var tables = ReadTableDirectory(data, numTables);
            bool isVariable = tables.ContainsKey("fvar");
            var ranges = new List<CodepointRange>();
            if (tables.TryGetValue("cmap", out (int Offset, int Length) cmap))
            {
                ReadCmap(data, cmap.Offset, ranges);
            }

            return new FontSnapshot(true, null, isVariable, ranges);
        }

        private static Dictionary<string, (int Offset, int Length)> ReadTableDirectory(byte[] data, int numTables)
        {
            var tables = new Dictionary<string, (int, int)>(numTables, StringComparer.Ordinal);
            for (int i = 0; i < numTables; i++)
            {
                int rec = 12 + i * 16;
                string tag = ReadTag(data, rec);
                int offset = (int)ReadUInt32BE(data, rec + 8);
                int length = (int)ReadUInt32BE(data, rec + 12);
                tables[tag] = (offset, length);
            }

            return tables;
        }

        private static void ReadCmap(byte[] data, int cmapOff, List<CodepointRange> ranges)
        {
            if (cmapOff < 0 || cmapOff + 4 > data.Length)
            {
                return;
            }

            int numSubtables = ReadUInt16BE(data, cmapOff + 2);
            for (int i = 0; i < numSubtables; i++)
            {
                int recOff = cmapOff + 4 + i * 8;
                if (recOff + 8 > data.Length)
                {
                    break;
                }

                int subOff = cmapOff + (int)ReadUInt32BE(data, recOff + 4);
                if (subOff < 0 || subOff + 4 > data.Length)
                {
                    continue;
                }

                int format = ReadUInt16BE(data, subOff);
                if (format == 4)
                {
                    ReadCmapFormat4(data, subOff, ranges);
                }
                else if (format == 12)
                {
                    ReadCmapFormat12(data, subOff, ranges);
                }
            }
        }

        private static void ReadCmapFormat4(byte[] data, int subOff, List<CodepointRange> ranges)
        {
            if (subOff + 14 > data.Length)
            {
                return;
            }

            int segCount = ReadUInt16BE(data, subOff + 6) / 2;
            int endPos = subOff + 14;
            int startPos = endPos + segCount * 2 + 2;
            if (startPos + segCount * 2 > data.Length)
            {
                return;
            }

            for (int s = 0; s < segCount; s++)
            {
                int start = ReadUInt16BE(data, startPos + s * 2);
                int end = ReadUInt16BE(data, endPos + s * 2);
                if (start == 0xFFFF && end == 0xFFFF)
                {
                    continue;
                }

                ranges.Add(new CodepointRange(start, end));
            }
        }

        private static void ReadCmapFormat12(byte[] data, int subOff, List<CodepointRange> ranges)
        {
            if (subOff + 16 > data.Length)
            {
                return;
            }

            uint numGroups = ReadUInt32BE(data, subOff + 12);
            int grpOff = subOff + 16;
            for (uint g = 0; g < numGroups; g++)
            {
                if (grpOff + 12 > data.Length)
                {
                    break;
                }

                int start = (int)ReadUInt32BE(data, grpOff);
                int end = (int)ReadUInt32BE(data, grpOff + 4);
                ranges.Add(new CodepointRange(start, end));
                grpOff += 12;
            }
        }

        private static int ReadUInt16BE(byte[] data, int offset)
        {
            return (data[offset] << 8) | data[offset + 1];
        }

        private static uint ReadUInt32BE(byte[] data, int offset)
        {
            return ((uint)data[offset] << 24)
                | ((uint)data[offset + 1] << 16)
                | ((uint)data[offset + 2] << 8)
                | data[offset + 3];
        }

        private static string ReadTag(byte[] data, int offset)
        {
            return string.Concat(
                (char)data[offset],
                (char)data[offset + 1],
                (char)data[offset + 2],
                (char)data[offset + 3]);
        }
    }
}
