using CodeWalker.GameFiles;
using NAudio.Wave;
using System;
using System.IO;
using System.Linq;

namespace ToolKitV.Models
{
    public static class AudioOptimizer
    {
        public struct OptimizationResult
        {
            public int FilesProcessed;
            public long BytesSaved;
            public bool Success;
            public string ErrorMessage;
        }

        public static OptimizationResult OptimizeFolder(string folderPath, int targetSampleRate = 24000)
        {
            var result = new OptimizationResult();
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                result.ErrorMessage = "Directory does not exist.";
                return result;
            }

            try
            {
                string[] awcFiles = Directory.GetFiles(folderPath, "*.awc", SearchOption.AllDirectories);
                
                foreach (var file in awcFiles)
                {
                    long originalSize = new FileInfo(file).Length;
                    if (OptimizeAwc(file, targetSampleRate))
                    {
                        long newSize = new FileInfo(file).Length;
                        if (newSize < originalSize)
                        {
                            result.BytesSaved += (originalSize - newSize);
                        }
                        result.FilesProcessed++;
                    }
                }
                
                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        private static bool OptimizeAwc(string filePath, int targetSampleRate)
        {
            try
            {
                byte[] data = File.ReadAllBytes(filePath);
                AwcFile awc = new AwcFile();
                awc.Load(data, null);

                bool modified = false;

                if (awc.Streams == null) return false;

                foreach (var stream in awc.Streams)
                {
                    if (stream.SamplesPerSecond > targetSampleRate)
                    {
                        byte[] wavData = stream.GetWavFile();
                        if (wavData == null) continue;

                        byte[] resampledWav = ResampleWav(wavData, targetSampleRate);
                        
                        // We only want to replace if we actually shrank the data
                        if (resampledWav != null && resampledWav.Length < wavData.Length)
                        {
                            stream.ParseWavFile(resampledWav);
                            modified = true;
                        }
                    }
                }

                if (modified)
                {
                    awc.BuildStreamInfos();
                    awc.BuildStreamDict();
                    awc.BuildChunkIndices();
                    
                    byte[] newData = awc.Save();
                    File.WriteAllBytes(filePath, newData);
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private static byte[] ResampleWav(byte[] wavData, int targetRate)
        {
            using (var ms = new MemoryStream(wavData))
            using (var reader = new WaveFileReader(ms))
            {
                var targetFormat = new WaveFormat(targetRate, reader.WaveFormat.BitsPerSample, reader.WaveFormat.Channels);
                using (var resampler = new MediaFoundationResampler(reader, targetFormat))
                {
                    resampler.ResamplerQuality = 60; // highest quality
                    using (var outMs = new MemoryStream())
                    {
                        WaveFileWriter.WriteWavFileToStream(outMs, resampler);
                        return outMs.ToArray();
                    }
                }
            }
        }
    }
}
