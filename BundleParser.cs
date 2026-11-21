namespace PCLInjector;

/// <summary>内嵌 PE 映像的描述。</summary>
public sealed class EmbeddedImage
{
    public required int FileOffset { get; init; }   // 在 exe 中的起始文件偏移
    public required byte[] Data { get; init; }      // 从该偏移截取的映像字节（供 dnlib 加载）
    public required int Size { get; init; }
}

/// <summary>
/// 动态扫描 exe 内的所有 PE 映像（DOS MZ 头 + PE 签名）。
/// 单文件 .NET 程序把程序集按原始字节嵌入，因此可按 PE 头特征发现全部内嵌 dll，无需预先知道布局或偏移。
/// </summary>
public static class BundleParser
{
    public static List<EmbeddedImage> Scan(byte[] data)
    {
        var result = new List<EmbeddedImage>();
        int len = data.Length;

        for (int pos = 0; pos < len - 0x40; pos++)
        {
            // DOS 头 'MZ'
            if (data[pos] != (byte)'M' || data[pos + 1] != (byte)'Z')
                continue;

            int peOffset = BitConverter.ToInt32(data, pos + 0x3C);
            if (peOffset <= 0 || pos + peOffset + 24 > len)
                continue;

            // PE 签名 'PE\0\0'
            if (data[pos + peOffset] != (byte)'P' ||
                data[pos + peOffset + 1] != (byte)'E' ||
                data[pos + peOffset + 2] != 0x00 ||
                data[pos + peOffset + 3] != 0x00)
                continue;

            int coff = pos + peOffset;
            int optionalHeader = coff + 24;
            if (optionalHeader + 24 > len)
                continue;

            ushort magic = BitConverter.ToUInt16(data, optionalHeader);
            int sizeOfImageOffset = magic switch
            {
                0x10B => 56, // PE32
                0x20B => 56, // PE32+
                _ => -1
            };
            if (sizeOfImageOffset < 0)
                continue;

            if (optionalHeader + sizeOfImageOffset + 4 > len)
                continue;
            uint sizeOfImage = BitConverter.ToUInt32(data, optionalHeader + sizeOfImageOffset);

            // 取 min(SizeOfImage, 剩余字节)，作为该映像的数据视图
            int available = len - pos;
            int take = (int)Math.Min(sizeOfImage == 0 ? available : sizeOfImage, available);
            if (take < 0x200)
                continue;

            var slice = new byte[take];
            Buffer.BlockCopy(data, pos, slice, 0, take);

            result.Add(new EmbeddedImage
            {
                FileOffset = pos,
                Size = take,
                Data = slice
            });
        }

        return result;
    }
}
