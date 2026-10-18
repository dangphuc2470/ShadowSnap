using System.IO;
using System.Media;
using System.Runtime.InteropServices;

namespace MacWindowSnap;

internal static class SoundHelper
{
    private static byte[]? _shutterWavBytes;

    public static void PlayCameraShutter()
    {
        try
        {
            // Try Windows camera shutter audio if present
            string systemCameraSound = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "Media",
                "Windows Hardware Insert.wav");

            if (File.Exists(systemCameraSound))
            {
                using SoundPlayer player = new(systemCameraSound);
                player.Play();
                return;
            }

            // Fallback: synthesized crisp click sound
            _shutterWavBytes ??= GenerateCrispClickWav();
            GCHandle pinned = GCHandle.Alloc(_shutterWavBytes, GCHandleType.Pinned);
            try
            {
                NativeMethods.PlaySound(pinned.AddrOfPinnedObject().ToString(), nint.Zero,
                    NativeMethods.SND_MEMORY | NativeMethods.SND_ASYNC | NativeMethods.SND_NODEFAULT);
            }
            finally
            {
                pinned.Free();
            }
        }
        catch
        {
            SystemSounds.Asterisk.Play();
        }
    }

    private static byte[] GenerateCrispClickWav()
    {
        int sampleRate = 44100;
        int durationMs = 60;
        int numSamples = (sampleRate * durationMs) / 1000;
        short[] samples = new short[numSamples];

        Random rand = new();
        for (int i = 0; i < numSamples; i++)
        {
            double progress = (double)i / numSamples;
            double decay = Math.Exp(-progress * 15.0);
            double freq1 = 2200.0 - (progress * 1200.0);
            double tone = Math.Sin(2.0 * Math.PI * freq1 * i / sampleRate);
            double noise = (rand.NextDouble() * 2.0 - 1.0) * 0.4;
            samples[i] = (short)(Math.Clamp((tone + noise) * decay, -1.0, 1.0) * 26000);
        }

        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms);

        // RIFF header
        writer.Write("RIFF"u8);
        writer.Write(36 + (numSamples * 2));
        writer.Write("WAVE"u8);

        // fmt chunk
        writer.Write("fmt "u8);
        writer.Write(16); // subchunk1 size
        writer.Write((short)1); // PCM
        writer.Write((short)1); // Mono
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2); // Byte rate
        writer.Write((short)2); // Block align
        writer.Write((short)16); // Bits per sample

        // data chunk
        writer.Write("data"u8);
        writer.Write(numSamples * 2);
        for (int i = 0; i < numSamples; i++)
        {
            writer.Write(samples[i]);
        }

        return ms.ToArray();
    }
}
