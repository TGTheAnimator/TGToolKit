using System;
using System.IO;

namespace ToolKitV.Models
{
    public static class Rsc7SizeHelper
    {
        public static float FlagToSize(int flag) =>
            (((flag >> 17) & 0x7f) + (((flag >> 11) & 0x3f) << 1) +
             (((flag >> 7)  & 0xf)  << 2) + (((flag >> 5)  & 0x3)  << 3) +
             (((flag >> 4)  & 0x1)  << 4)) * (0x2000 << (flag & 0xF));

        /// <summary>
        /// Reads the RSC7 header and returns the virtual size (VRAM budget used by FiveM) 
        /// and physical size (actual disk file size) in MB.
        /// If the file is not RSC7, virtualMB will be 0f.
        /// </summary>
        public static (float virtualMB, float diskMB) GetFileSize(string filePath)
        {
            try
            {
                float diskMB = new FileInfo(filePath).Length / 1024f / 1024f;

                using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                
                if (fs.Length < 16) return (0f, diskMB);

                using BinaryReader reader = new(fs);

                byte[] magic = reader.ReadBytes(4);
                string magStr = System.Text.Encoding.ASCII.GetString(magic);

                if (magStr != "RSC7")
                    return (0f, diskMB);

                reader.ReadBytes(4); // version

                int virtualFlag = reader.ReadInt32();
                float vMB = FlagToSize(virtualFlag) / 1024f / 1024f;

                return (vMB, diskMB);
            }
            catch
            {
                return (0f, 0f);
            }
        }
    }
}
