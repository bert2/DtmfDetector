﻿namespace DtmfDetection {
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using DtmfDetection.Interfaces;

    /// <summary>Creates a `Goertzel` accumulator for each of the DTMF tone low (697, 770, 852, and 941 Hz) and high frequencies (1209, 1336, 1477, and 1633 Hz) and repeats that for each audio channel in the input data. When `Detect()` is called, each sample of the input sample block is added to each `Goertzel` accumulator and afterwards the Goertzel response of each frequency is retrieved. Reports a detected DTMF tone when exactly one of the four low frequency responses crosses the detection threshold, and exactly one of the four high frequency responses crosses the detection threshold.</summary>
    public class Detector : IDetector {
        private static readonly IReadOnlyList<int> lowTones = new[] { 697, 770, 852, 941 };
        private static readonly IReadOnlyList<int> highTones = new[] { 1209, 1336, 1477, 1633 };
        private readonly IReadOnlyList<Goertzel> initLoGoertz;
        private readonly IReadOnlyList<Goertzel> initHiGoertz;

        /// <summary>The number of channels this detector has been created for. Used by the `Analyzer` to validate that this detector supports the number of channels present int the source data (`ISamples.Channels`).</summary>
        public int Channels { get; }

        /// <summary>The `Config` this detector has been created with.</summary>
        public Config Config { get; }

        /// <summary>Creates a new `Detector` for the given number of audio channels and with the given dector config.</summary>
        /// <param name="channels">The number of channels in the input sample data.</param>
        /// <param name="config">The detector config.</param>
        public Detector(int channels, in Config config) {
            Channels = channels;
            Config = config;

            var sampleRate = config.SampleRate;
            var numSamples = config.SampleBlockSize;
            initLoGoertz = lowTones.Select(f => Goertzel.Init(f, sampleRate, numSamples)).ToArray();
            initHiGoertz = highTones.Select(f => Goertzel.Init(f, sampleRate, numSamples)).ToArray();
        }

        /// <summary>Runs the Goertzel algorithm on all samples in `sampleBlock` and returns the DTMF key detected in each channel. `PhoneKey.None` is used in case no DTMF key has been detected in a channel.</summary>
        /// <param name="sampleBlock">The block of samples to analyze. Its length should always match `Config.SampleBlockSize * Detector.Channels` except when the end of the input has been reached, in which case it might be smalller once.</param>
        /// <returns>A list of DTMF keys, one for each channel. Hence its length will match the value of `Detector.Channels`.</returns>
        public IReadOnlyList<PhoneKey> Detect(in ReadOnlySpan<float> sampleBlock) {
            var loGoertz = CreateGoertzels(initLoGoertz, Channels);
            var hiGoertz = CreateGoertzels(initHiGoertz, Channels);
            AddSamples(sampleBlock, Channels, loGoertz, hiGoertz);
            return Detect(loGoertz, hiGoertz, Config.Threshold, Channels);
        }

        private static Goertzel[][] CreateGoertzels(IReadOnlyList<Goertzel> initGoertz, int channels) {
            var goertz = new Goertzel[channels][];

            for (var c = 0; c < channels; c++) {
                goertz[c] = new[] { initGoertz[0], initGoertz[1], initGoertz[2], initGoertz[3] };
            }

            return goertz;
        }

        private static void AddSamples(in ReadOnlySpan<float> sampleBlock, int channels, Goertzel[][] loGoertz, Goertzel[][] hiGoertz) {
            for (var i = 0; i < sampleBlock.Length; i++) {
                var c = i % channels;

                loGoertz[c][0] = loGoertz[c][0].AddSample(sampleBlock[i]);
                loGoertz[c][1] = loGoertz[c][1].AddSample(sampleBlock[i]);
                loGoertz[c][2] = loGoertz[c][2].AddSample(sampleBlock[i]);
                loGoertz[c][3] = loGoertz[c][3].AddSample(sampleBlock[i]);

                hiGoertz[c][0] = hiGoertz[c][0].AddSample(sampleBlock[i]);
                hiGoertz[c][1] = hiGoertz[c][1].AddSample(sampleBlock[i]);
                hiGoertz[c][2] = hiGoertz[c][2].AddSample(sampleBlock[i]);
                hiGoertz[c][3] = hiGoertz[c][3].AddSample(sampleBlock[i]);
            }
        }

        private PhoneKey[] Detect(
            IReadOnlyList<IReadOnlyList<Goertzel>> loGoertz,
            IReadOnlyList<IReadOnlyList<Goertzel>> hiGoertz,
            double threshold,
            int channels) {
            var phoneKeys = new PhoneKey[channels];

            for (var c = 0; c < channels; c++) {
                phoneKeys[c] = Detect(loGoertz[c], hiGoertz[c], threshold);
            }

            return phoneKeys;
        }

        private PhoneKey Detect(IReadOnlyList<Goertzel> loGoertz, IReadOnlyList<Goertzel> hiGoertz, double threshold) {
            var (fstLoIdx, sndLoIdx) = FindMaxTwo(loGoertz);
            var (fstLoVal, sndLoVal) = (Response(loGoertz[fstLoIdx]), Response(loGoertz[sndLoIdx]));

            var (fstHiIdx, sndHiIdx) = FindMaxTwo(hiGoertz);
            var (fstHiVal, sndHiVal) = (Response(hiGoertz[fstHiIdx]), Response(hiGoertz[sndHiIdx]));

            //Console.WriteLine($"lo: {fstLoIdx}: {fstLoVal,8:N3}, {sndLoIdx}: {sndLoVal,8:N3}  |  hi: {fstHiIdx}: {fstHiVal,8:N3}, {sndHiIdx}: {sndHiVal,8:N3}");

            return fstLoVal < threshold || fstHiVal < threshold
                || fstLoVal > threshold && sndLoVal > threshold
                || fstHiVal > threshold && sndHiVal > threshold
                || double.IsNaN(fstLoVal) || double.IsNaN(fstHiVal)
                ? PhoneKey.None
                : (highTones[fstHiIdx], lowTones[fstLoIdx]).ToPhoneKey();
        }

        private (int fstIdx, int sndIdx) FindMaxTwo(IReadOnlyList<Goertzel> goertz) {
            int fst = 0, snd = 1;

            if (Response(goertz[1]) > Response(goertz[0])) {
                snd = 0;
                fst = 1;
            }

            if (Response(goertz[2]) > Response(goertz[fst])) {
                snd = fst;
                fst = 2;
            } else if (Response(goertz[2]) > Response(goertz[snd])) {
                snd = 2;
            }

            if (Response(goertz[3]) > Response(goertz[fst])) {
                snd = fst;
                fst = 3;
            } else if (Response(goertz[3]) > Response(goertz[snd])) {
                snd = 3;
            }

            return (fst, snd);
        }

        private double Response(in Goertzel g) => Config.NormalizeResponse ? g.NormResponse : g.Response;
    }
}
