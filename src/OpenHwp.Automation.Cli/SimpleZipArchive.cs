using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace OpenHwp.Automation.Cli
{
    internal static class SimpleZipArchive
    {
        private const uint EndOfCentralDirectorySignature = 0x06054b50;
        private const uint CentralDirectorySignature = 0x02014b50;
        private const uint LocalFileHeaderSignature = 0x04034b50;
        private const ushort StoredMethod = 0;
        private const ushort DeflatedMethod = 8;
        private const ushort Utf8FileNameFlag = 0x0800;

        public static IDictionary<string, byte[]> ReadAll(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var eocdOffset = FindEndOfCentralDirectory(bytes);
            var entryCount = ReadUInt16(bytes, eocdOffset + 10);
            var centralDirectoryOffset = ReadUInt32(bytes, eocdOffset + 16);
            var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var offset = checked((int)centralDirectoryOffset);

            for (var index = 0; index < entryCount; index++)
            {
                if (ReadUInt32(bytes, offset) != CentralDirectorySignature)
                {
                    throw new InvalidDataException("Invalid ZIP central directory entry.");
                }

                var flags = ReadUInt16(bytes, offset + 8);
                var method = ReadUInt16(bytes, offset + 10);
                var compressedSize = ReadUInt32(bytes, offset + 20);
                var uncompressedSize = ReadUInt32(bytes, offset + 24);
                var nameLength = ReadUInt16(bytes, offset + 28);
                var extraLength = ReadUInt16(bytes, offset + 30);
                var commentLength = ReadUInt16(bytes, offset + 32);
                var localHeaderOffset = ReadUInt32(bytes, offset + 42);
                var nameBytes = Slice(bytes, offset + 46, nameLength);
                var name = DecodeFileName(nameBytes, flags);

                entries[name] = ReadEntryData(bytes, checked((int)localHeaderOffset), method, checked((int)compressedSize), checked((int)uncompressedSize));
                offset += 46 + nameLength + extraLength + commentLength;
            }

            return entries;
        }

        public static void WriteAll(string path, IDictionary<string, byte[]> entries)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Archive path is required.", nameof(path));
            }

            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var output = File.Create(path))
            using (var writer = new BinaryWriter(output))
            {
                var centralDirectoryEntries = new List<CentralDirectoryEntry>();
                foreach (var name in OrderedEntryNames(entries))
                {
                    var content = entries[name] ?? new byte[0];
                    var method = string.Equals(name, "mimetype", StringComparison.Ordinal) ? StoredMethod : DeflatedMethod;
                    var payload = method == StoredMethod ? content : Deflate(content);
                    var crc = ComputeCrc32(content);
                    var nameBytes = Encoding.UTF8.GetBytes(name);
                    var localHeaderOffset = checked((uint)output.Position);
                    var now = DateTime.Now;
                    var dosTime = ToDosTime(now);
                    var dosDate = ToDosDate(now);

                    WriteLocalHeader(writer, method, crc, checked((uint)payload.Length), checked((uint)content.Length), nameBytes, dosTime, dosDate);
                    writer.Write(nameBytes);
                    writer.Write(payload);

                    centralDirectoryEntries.Add(new CentralDirectoryEntry(
                        nameBytes,
                        method,
                        crc,
                        checked((uint)payload.Length),
                        checked((uint)content.Length),
                        localHeaderOffset,
                        dosTime,
                        dosDate));
                }

                var centralDirectoryOffset = checked((uint)output.Position);
                foreach (var entry in centralDirectoryEntries)
                {
                    WriteCentralDirectoryHeader(writer, entry);
                    writer.Write(entry.NameBytes);
                }

                var centralDirectorySize = checked((uint)(output.Position - centralDirectoryOffset));
                WriteEndOfCentralDirectory(writer, checked((ushort)centralDirectoryEntries.Count), centralDirectorySize, centralDirectoryOffset);
            }
        }

        private static byte[] ReadEntryData(byte[] archive, int localHeaderOffset, ushort method, int compressedSize, int uncompressedSize)
        {
            if (ReadUInt32(archive, localHeaderOffset) != LocalFileHeaderSignature)
            {
                throw new InvalidDataException("Invalid ZIP local file header.");
            }

            var nameLength = ReadUInt16(archive, localHeaderOffset + 26);
            var extraLength = ReadUInt16(archive, localHeaderOffset + 28);
            var dataOffset = localHeaderOffset + 30 + nameLength + extraLength;
            var compressed = Slice(archive, dataOffset, compressedSize);

            if (method == StoredMethod)
            {
                return compressed;
            }

            if (method != DeflatedMethod)
            {
                throw new NotSupportedException("Unsupported ZIP compression method: " + method);
            }

            using (var input = new MemoryStream(compressed))
            using (var deflate = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream(uncompressedSize > 0 ? uncompressedSize : 0))
            {
                CopyTo(deflate, output);
                return output.ToArray();
            }
        }

        private static int FindEndOfCentralDirectory(byte[] bytes)
        {
            var minimumOffset = Math.Max(0, bytes.Length - 22 - ushort.MaxValue);
            for (var offset = bytes.Length - 22; offset >= minimumOffset; offset--)
            {
                if (ReadUInt32(bytes, offset) == EndOfCentralDirectorySignature)
                {
                    return offset;
                }
            }

            throw new InvalidDataException("ZIP end of central directory was not found.");
        }

        private static string DecodeFileName(byte[] bytes, ushort flags)
        {
            return (flags & 0x0800) != 0 ? Encoding.UTF8.GetString(bytes) : Encoding.ASCII.GetString(bytes);
        }

        private static IEnumerable<string> OrderedEntryNames(IDictionary<string, byte[]> entries)
        {
            if (entries.ContainsKey("mimetype"))
            {
                yield return "mimetype";
            }

            foreach (var name in entries.Keys.Where(name => !string.Equals(name, "mimetype", StringComparison.Ordinal)).OrderBy(name => name, StringComparer.Ordinal))
            {
                yield return name;
            }
        }

        private static byte[] Deflate(byte[] content)
        {
            using (var output = new MemoryStream())
            {
                using (var deflate = new DeflateStream(output, CompressionMode.Compress))
                {
                    deflate.Write(content, 0, content.Length);
                }

                return output.ToArray();
            }
        }

        private static void WriteLocalHeader(BinaryWriter writer, ushort method, uint crc, uint compressedSize, uint uncompressedSize, byte[] nameBytes, ushort dosTime, ushort dosDate)
        {
            writer.Write(LocalFileHeaderSignature);
            writer.Write((ushort)20);
            writer.Write(Utf8FileNameFlag);
            writer.Write(method);
            writer.Write(dosTime);
            writer.Write(dosDate);
            writer.Write(crc);
            writer.Write(compressedSize);
            writer.Write(uncompressedSize);
            writer.Write(checked((ushort)nameBytes.Length));
            writer.Write((ushort)0);
        }

        private static void WriteCentralDirectoryHeader(BinaryWriter writer, CentralDirectoryEntry entry)
        {
            writer.Write(CentralDirectorySignature);
            writer.Write((ushort)20);
            writer.Write((ushort)20);
            writer.Write(Utf8FileNameFlag);
            writer.Write(entry.Method);
            writer.Write(entry.DosTime);
            writer.Write(entry.DosDate);
            writer.Write(entry.Crc);
            writer.Write(entry.CompressedSize);
            writer.Write(entry.UncompressedSize);
            writer.Write(checked((ushort)entry.NameBytes.Length));
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((uint)0);
            writer.Write(entry.LocalHeaderOffset);
        }

        private static void WriteEndOfCentralDirectory(BinaryWriter writer, ushort entryCount, uint centralDirectorySize, uint centralDirectoryOffset)
        {
            writer.Write(EndOfCentralDirectorySignature);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write(entryCount);
            writer.Write(entryCount);
            writer.Write(centralDirectorySize);
            writer.Write(centralDirectoryOffset);
            writer.Write((ushort)0);
        }

        private static ushort ToDosTime(DateTime dateTime)
        {
            return (ushort)((dateTime.Hour << 11) | (dateTime.Minute << 5) | (dateTime.Second / 2));
        }

        private static ushort ToDosDate(DateTime dateTime)
        {
            return (ushort)(((dateTime.Year - 1980) << 9) | (dateTime.Month << 5) | dateTime.Day);
        }

        private static uint ComputeCrc32(byte[] content)
        {
            uint crc = 0xffffffff;
            foreach (var value in content)
            {
                crc = Crc32Table[(crc ^ value) & 0xff] ^ (crc >> 8);
            }

            return ~crc;
        }

        private static readonly uint[] Crc32Table = BuildCrc32Table();

        private static uint[] BuildCrc32Table()
        {
            var table = new uint[256];
            for (uint index = 0; index < table.Length; index++)
            {
                var value = index;
                for (var bit = 0; bit < 8; bit++)
                {
                    value = (value & 1) == 1 ? 0xedb88320 ^ (value >> 1) : value >> 1;
                }

                table[index] = value;
            }

            return table;
        }

        private static byte[] Slice(byte[] source, int offset, int length)
        {
            var result = new byte[length];
            Buffer.BlockCopy(source, offset, result, 0, length);
            return result;
        }

        private static ushort ReadUInt16(byte[] bytes, int offset)
        {
            return BitConverter.ToUInt16(bytes, offset);
        }

        private static uint ReadUInt32(byte[] bytes, int offset)
        {
            return BitConverter.ToUInt32(bytes, offset);
        }

        private static void CopyTo(Stream input, Stream output)
        {
            var buffer = new byte[81920];
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
            }
        }

        private struct CentralDirectoryEntry
        {
            public CentralDirectoryEntry(byte[] nameBytes, ushort method, uint crc, uint compressedSize, uint uncompressedSize, uint localHeaderOffset, ushort dosTime, ushort dosDate)
            {
                NameBytes = nameBytes;
                Method = method;
                Crc = crc;
                CompressedSize = compressedSize;
                UncompressedSize = uncompressedSize;
                LocalHeaderOffset = localHeaderOffset;
                DosTime = dosTime;
                DosDate = dosDate;
            }

            public byte[] NameBytes { get; private set; }

            public ushort Method { get; private set; }

            public uint Crc { get; private set; }

            public uint CompressedSize { get; private set; }

            public uint UncompressedSize { get; private set; }

            public uint LocalHeaderOffset { get; private set; }

            public ushort DosTime { get; private set; }

            public ushort DosDate { get; private set; }
        }
    }
}
