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
        private readonly struct Format4Tables
        {
            public readonly long EndPos;
            public readonly long StartPos;
            public readonly long DeltaPos;
            public readonly long RangeOffsetPos;

            public Format4Tables(long endPos, long startPos, long deltaPos, long rangeOffsetPos)
            {
                EndPos = endPos;
                StartPos = startPos;
                DeltaPos = deltaPos;
                RangeOffsetPos = rangeOffsetPos;
            }
        }

        private readonly struct Format4Segment
        {
            public readonly int Start;
            public readonly int End;
            public readonly short IdDelta;
            public readonly int IdRangeOffset;

            public Format4Segment(int start, int end, short idDelta, int idRangeOffset)
            {
                Start = start;
                End = end;
                IdDelta = idDelta;
                IdRangeOffset = idRangeOffset;
            }
        }

        public static FontSnapshot ReadFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return new FontSnapshot(false, "Font file does not exist.", false, Array.Empty<CodepointRange>());
                }

                return ReadBytes(File.ReadAllBytes(path));
            }
            catch (IOException)
            {
                return new FontSnapshot(false, "Unable to read font file.", false, Array.Empty<CodepointRange>());
            }
            catch (UnauthorizedAccessException)
            {
                return new FontSnapshot(false, "Unable to access font file.", false, Array.Empty<CodepointRange>());
            }
        }

        public static FontSnapshot ReadBytes(byte[] data)
        {
            if (data == null || data.Length < 12)
            {
                return new FontSnapshot(false, "File too small to be a valid SFNT font.", false, Array.Empty<CodepointRange>());
            }

            uint sfnt = ReadUInt32BE(data, 0L);
            if (sfnt != 0x00010000u && sfnt != 0x4F54544Fu && sfnt != 0x74727565u)
            {
                return new FontSnapshot(false, "Invalid TTF/OTF SFNT header.", false, Array.Empty<CodepointRange>());
            }

            int numTables = ReadUInt16BE(data, 4L);
            if (data.Length < 12L + numTables * 16L)
            {
                return new FontSnapshot(false, "Truncated SFNT table directory.", false, Array.Empty<CodepointRange>());
            }

            var tables = ReadTableDirectory(data, numTables);
            bool isVariable = tables.ContainsKey("fvar");
            var ranges = new List<CodepointRange>();
            if (tables.TryGetValue("cmap", out (long Offset, long Length) cmap))
            {
                ReadCmap(data, cmap.Offset, cmap.Length, ranges);
            }

            return new FontSnapshot(true, null, isVariable, ranges);
        }

        private static Dictionary<string, (long Offset, long Length)> ReadTableDirectory(byte[] data, int numTables)
        {
            var tables = new Dictionary<string, (long, long)>(numTables, StringComparer.Ordinal);
            for (int i = 0; i < numTables; i++)
            {
                long rec = 12L + i * 16L;
                string tag = ReadTag(data, rec);
                long offset = ReadUInt32BE(data, rec + 8L);
                long length = ReadUInt32BE(data, rec + 12L);
                tables[tag] = (offset, length);
            }

            return tables;
        }

        private static void ReadCmap(byte[] data, long cmapOff, long cmapLen, List<CodepointRange> ranges)
        {
            if (cmapOff < 0L || cmapLen < 4L || cmapOff + 4L > data.Length)
            {
                return;
            }

            int numSubtables = ReadUInt16BE(data, cmapOff + 2L);
            for (int i = 0; i < numSubtables; i++)
            {
                long recOff = cmapOff + 4L + i * 8L;
                if (recOff + 8L > data.Length)
                {
                    break;
                }

                long subOff = cmapOff + ReadUInt32BE(data, recOff + 4L);
                if (subOff < 0L || subOff + 4L > data.Length)
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

        private static void ReadCmapFormat4(byte[] data, long subOff, List<CodepointRange> ranges)
        {
            if (subOff + 14L > data.Length)
            {
                return;
            }

            int segCountX2 = ReadUInt16BE(data, subOff + 6L);
            if (segCountX2 < 2 || (segCountX2 & 1) != 0)
            {
                return;
            }

            int segCount = segCountX2 / 2;
            long endPos = subOff + 14L;
            long startPos = endPos + segCount * 2L + 2L;
            long deltaPos = startPos + segCount * 2L;
            long rangeOffsetPos = deltaPos + segCount * 2L;
            if (rangeOffsetPos + segCount * 2L > data.Length)
            {
                return;
            }

            var tables = new Format4Tables(endPos, startPos, deltaPos, rangeOffsetPos);
            for (int s = 0; s < segCount; s++)
            {
                AddFormat4SegmentRanges(data, tables, s, ranges);
            }
        }

        private static void AddFormat4SegmentRanges(
            byte[] data,
            Format4Tables tables,
            int segmentIndex,
            List<CodepointRange> ranges)
        {
            long off = segmentIndex * 2L;
            int start = ReadUInt16BE(data, tables.StartPos + off);
            int end = ReadUInt16BE(data, tables.EndPos + off);
            if ((start == 0xFFFF && end == 0xFFFF) || end < start)
            {
                return;
            }

            short idDelta = (short)ReadUInt16BE(data, tables.DeltaPos + off);
            int idRangeOffset = ReadUInt16BE(data, tables.RangeOffsetPos + off);
            var seg = new Format4Segment(start, end, idDelta, idRangeOffset);
            if (idRangeOffset == 0)
            {
                AddDirectDeltaRanges(seg, ranges);
                return;
            }

            long glyphBaseOff = tables.RangeOffsetPos + off + idRangeOffset;
            AddIndexedGlyphRanges(data, glyphBaseOff, seg, ranges);
        }

        private static void AddDirectDeltaRanges(Format4Segment seg, List<CodepointRange> ranges)
        {
            int zeroCp = (-seg.IdDelta) & 0xFFFF;
            if (zeroCp < seg.Start || zeroCp > seg.End)
            {
                ranges.Add(new CodepointRange(seg.Start, seg.End));
                return;
            }

            if (zeroCp > seg.Start)
            {
                ranges.Add(new CodepointRange(seg.Start, zeroCp - 1));
            }

            if (zeroCp < seg.End)
            {
                ranges.Add(new CodepointRange(zeroCp + 1, seg.End));
            }
        }

        private static void AddIndexedGlyphRanges(
            byte[] data,
            long glyphBaseOff,
            Format4Segment seg,
            List<CodepointRange> ranges)
        {
            int runStart = -1;
            for (int cp = seg.Start; cp <= seg.End; cp++)
            {
                long glyphIdxOff = glyphBaseOff + 2L * (cp - seg.Start);
                if (glyphIdxOff < 0L || glyphIdxOff + 2L > data.Length)
                {
                    break;
                }

                int rawGlyph = ReadUInt16BE(data, glyphIdxOff);
                int glyphId = rawGlyph == 0 ? 0 : ((rawGlyph + seg.IdDelta) & 0xFFFF);
                if (glyphId != 0)
                {
                    if (runStart < 0)
                    {
                        runStart = cp;
                    }
                }
                else if (runStart >= 0)
                {
                    ranges.Add(new CodepointRange(runStart, cp - 1));
                    runStart = -1;
                }
            }

            if (runStart >= 0)
            {
                ranges.Add(new CodepointRange(runStart, seg.End));
            }
        }

        private static void ReadCmapFormat12(byte[] data, long subOff, List<CodepointRange> ranges)
        {
            if (subOff + 16L > data.Length)
            {
                return;
            }

            uint numGroups = ReadUInt32BE(data, subOff + 12L);
            long grpOff = subOff + 16L;
            for (uint g = 0; g < numGroups; g++)
            {
                if (grpOff + 12L > data.Length)
                {
                    break;
                }

                uint startU = ReadUInt32BE(data, grpOff);
                uint endU = ReadUInt32BE(data, grpOff + 4L);
                uint startGlyph = ReadUInt32BE(data, grpOff + 8L);
                grpOff += 12L;

                if (endU < startU || startU > 0x10FFFFu)
                {
                    continue;
                }

                if (endU > 0x10FFFFu)
                {
                    endU = 0x10FFFFu;
                }

                if (startGlyph == 0u)
                {
                    if (startU == endU)
                    {
                        continue;
                    }

                    startU++;
                }

                ranges.Add(new CodepointRange((int)startU, (int)endU));
            }
        }

        private static int ReadUInt16BE(byte[] data, long offset)
        {
            return (data[offset] << 8) | data[offset + 1L];
        }

        private static uint ReadUInt32BE(byte[] data, long offset)
        {
            return ((uint)data[offset] << 24)
                | ((uint)data[offset + 1L] << 16)
                | ((uint)data[offset + 2L] << 8)
                | data[offset + 3L];
        }

        private static string ReadTag(byte[] data, long offset)
        {
            return string.Concat(
                (char)data[offset],
                (char)data[offset + 1L],
                (char)data[offset + 2L],
                (char)data[offset + 3L]);
        }
    }
}
