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
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                var centralDirectory = new List<CentralDirectoryRecord>();

                if (entries.ContainsKey("mimetype"))
                {
                    WriteEntry(writer, centralDirectory, "mimetype", entries["mimetype"]);
                }

                foreach (var pair in entries.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    if (string.Equals(pair.Key, "mimetype", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    WriteEntry(writer, centralDirectory, pair.Key, pair.Value);
                }

                var centralDirectoryStart = stream.Position;
                foreach (var record in centralDirectory)
                {
                    WriteCentralDirectoryRecord(writer, record);
                }

                var centralDirectorySize = stream.Position - centralDirectoryStart;
                WriteEndOfCentralDirectory(writer, centralDirectory.Count, centralDirectorySize, centralDirectoryStart);
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

        private static void WriteEntry(BinaryWriter writer, IList<CentralDirectoryRecord> centralDirectory, string name, byte[] data)
        {
            var nameBytes = Encoding.UTF8.GetBytes(name);
            var crc = Crc32.Compute(data);
            var offset = writer.BaseStream.Position;
            ushort dosTime;
            ushort dosDate;
            GetDosDateTime(DateTime.Now, out dosTime, out dosDate);

            writer.Write(LocalFileHeaderSignature);
            writer.Write((ushort)20);
            writer.Write((ushort)0x0800);
            writer.Write(StoredMethod);
            writer.Write(dosTime);
            writer.Write(dosDate);
            writer.Write(crc);
            writer.Write((uint)data.Length);
            writer.Write((uint)data.Length);
            writer.Write((ushort)nameBytes.Length);
            writer.Write((ushort)0);
            writer.Write(nameBytes);
            writer.Write(data);

            centralDirectory.Add(new CentralDirectoryRecord
            {
                Name = name,
                NameBytes = nameBytes,
                Crc = crc,
                Size = (uint)data.Length,
                CompressedSize = (uint)data.Length,
                LocalHeaderOffset = (uint)offset,
                DosTime = dosTime,
                DosDate = dosDate
            });
        }

        private static void WriteCentralDirectoryRecord(BinaryWriter writer, CentralDirectoryRecord record)
        {
            writer.Write(CentralDirectorySignature);
            writer.Write((ushort)20);
            writer.Write((ushort)20);
            writer.Write((ushort)0x0800);
            writer.Write(StoredMethod);
            writer.Write(record.DosTime);
            writer.Write(record.DosDate);
            writer.Write(record.Crc);
            writer.Write(record.CompressedSize);
            writer.Write(record.Size);
            writer.Write((ushort)record.NameBytes.Length);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((uint)0);
            writer.Write(record.LocalHeaderOffset);
            writer.Write(record.NameBytes);
        }

        private static void WriteEndOfCentralDirectory(BinaryWriter writer, int entryCount, long centralDirectorySize, long centralDirectoryStart)
        {
            writer.Write(EndOfCentralDirectorySignature);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)entryCount);
            writer.Write((ushort)entryCount);
            writer.Write((uint)centralDirectorySize);
            writer.Write((uint)centralDirectoryStart);
            writer.Write((ushort)0);
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

        private static void GetDosDateTime(DateTime dateTime, out ushort time, out ushort date)
        {
            var value = dateTime;
            if (value.Year < 1980)
            {
                value = new DateTime(1980, 1, 1);
            }

            time = (ushort)((value.Hour << 11) | (value.Minute << 5) | (value.Second / 2));
            date = (ushort)(((value.Year - 1980) << 9) | (value.Month << 5) | value.Day);
        }

        private sealed class CentralDirectoryRecord
        {
            public string Name { get; set; }

            public byte[] NameBytes { get; set; }

            public uint Crc { get; set; }

            public uint Size { get; set; }

            public uint CompressedSize { get; set; }

            public uint LocalHeaderOffset { get; set; }

            public ushort DosTime { get; set; }

            public ushort DosDate { get; set; }
        }

        private static class Crc32
        {
            private static readonly uint[] Table = BuildTable();

            public static uint Compute(byte[] bytes)
            {
                var crc = 0xffffffffu;
                foreach (var value in bytes)
                {
                    crc = (crc >> 8) ^ Table[(crc ^ value) & 0xff];
                }

                return crc ^ 0xffffffffu;
            }

            private static uint[] BuildTable()
            {
                var table = new uint[256];
                for (uint index = 0; index < table.Length; index++)
                {
                    var value = index;
                    for (var bit = 0; bit < 8; bit++)
                    {
                        value = (value & 1) == 1 ? 0xedb88320u ^ (value >> 1) : value >> 1;
                    }

                    table[index] = value;
                }

                return table;
            }
        }
    }
}
