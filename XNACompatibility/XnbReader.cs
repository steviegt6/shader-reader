#region License
/*
  ShaderDecompiler - Direct3D shader decompiler

  Released under Microsoft Public License
  See LICENSE for details
*/
#endregion

namespace ShaderDecompiler.XNACompatibility;

public static class XnbReader
{
    private static readonly byte[] xnb_magic = "XNB"u8.ToArray();

    public static bool CheckHeader(BinaryReader reader)
    {
        return xnb_magic.All(c => reader.ReadByte() == c);
    }

    public static Effect ReadEffect(BinaryReader reader)
    {
        if (!CheckHeader(reader))
        {
            throw new Exception("Not xnb data");
        }

        if (reader.ReadByte() != 119)
        {
            throw new Exception("Bad xnb platform");
        }

        var header = reader.ReadUInt16();
        var compressed = (header & 0x8000) != 0;

        var length = reader.ReadInt32();

        // https://github.com/FNA-XNA/FNA/blob/30cba4c463fab525843a86ffb7f2b4222a80410e/src/Content/ContentManager.cs#L497
        if (compressed)
        {
            var compressedSize = length - 14;
            var decompressedSize = reader.ReadInt32();
            var decompressedStream = new MemoryStream(new byte[decompressedSize], 0, decompressedSize, true, true);
            var compressedStream = new MemoryStream(reader.ReadBytes(compressedSize));
            var dec = new LzxDecoder(16);
            var pos = 0L;
            while (pos < compressedSize)
            {
                var num = compressedStream.ReadByte();
                var lo = compressedStream.ReadByte();
                var blockSize = (num << 8) | lo;
                var frameSize = 32768;
                if (num == 255)
                {
                    var num2 = lo;
                    lo = (byte)compressedStream.ReadByte();
                    frameSize = (num2 << 8) | lo;
                    int num3 = (byte)compressedStream.ReadByte();
                    lo = (byte)compressedStream.ReadByte();
                    blockSize = (num3 << 8) | lo;
                    pos += 5L;
                }
                else
                {
                    pos += 2L;
                }

                if (blockSize == 0 || frameSize == 0)
                {
                    break;
                }

                dec.Decompress(compressedStream, blockSize, decompressedStream, frameSize);
                pos += blockSize;
                compressedStream.Seek(pos, SeekOrigin.Begin);
            }

            if (decompressedStream.Position != decompressedSize)
            {
                throw new Exception("Decompression of xnb content failed. ");
            }

            decompressedStream.Seek(0L, SeekOrigin.Begin);
            compressedStream.Dispose();

            reader = new BinaryReader(decompressedStream);
        }

        var readerCount = reader.Read7BitEncodedInt();
        var readerTypes = new string[readerCount];
        for (var i = 0; i < readerCount; i++)
        {
            readerTypes[i] = reader.ReadString();
            reader.ReadInt32();
        }

        reader.Read7BitEncodedInt();

        var readerIndex = reader.Read7BitEncodedInt();

        if (readerIndex > readerCount || readerIndex < 1)
        {
            throw new Exception("Unknown content type");
        }

        if (!readerTypes[readerIndex - 1].Contains("EffectReader"))
        {
            throw new Exception("Not an xnb effect");
        }

        reader.ReadInt32();
        return Effect.Read(reader);
    }
}
