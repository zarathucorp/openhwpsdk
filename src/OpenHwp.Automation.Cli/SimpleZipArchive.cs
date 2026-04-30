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
    }
}
